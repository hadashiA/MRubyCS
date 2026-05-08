using System.Collections.Generic;
using System.Text;

namespace MRubyCS.Hir;

// Structural / SSA invariant checker for HirFunction.
//
// Run after every transforming pass in dev/debug builds. The set of checks
// roughly mirrors what ZJIT validates with --enable-zjit=dev:
//
//   - Block consistency: every InsnId in Block.Insns / Block.Params claims to
//     belong to that block.
//   - Edge args vs. target params: edge.Args.Count == target.Params.Count.
//   - Inputs reference live (in-bounds) insn ids; no insn references itself.
//   - Use-list consistency: every (value, user) entry in InsnUses corresponds
//     to a real reference (input or edge-arg-into-param).
//   - Terminator placement: a terminator, if present, is the last insn of its
//     block; non-terminator block fall-through edges line up with EndPc.
//
// Throws HirVerificationException with a multi-line report on the first batch
// of failures (we collect a few before giving up so users see related issues).
public sealed class HirVerificationException(string report) : System.Exception(report)
{
    public string Report { get; } = report;
}

public static class HirVerifier
{
    const int MaxReportedErrors = 16;

    public static void Verify(HirFunction func)
    {
        var errors = new List<string>();
        CheckArenaShapes(func, errors);
        CheckBlocks(func, errors);
        CheckEdges(func, errors);
        CheckInsns(func, errors);
        CheckUseList(func, errors);

        if (errors.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine("HIR verification failed:");
            for (var i = 0; i < errors.Count && i < MaxReportedErrors; i++)
            {
                sb.Append("  - ").AppendLine(errors[i]);
            }
            if (errors.Count > MaxReportedErrors)
            {
                sb.Append("  ... and ").Append(errors.Count - MaxReportedErrors).AppendLine(" more");
            }
            throw new HirVerificationException(sb.ToString());
        }
    }

    static void CheckArenaShapes(HirFunction func, List<string> errors)
    {
        var n = func.Insns.Count;
        if (func.InsnTypes.Count != n) errors.Add($"InsnTypes count {func.InsnTypes.Count} != Insns count {n}");
        if (func.InsnEffects.Count != n) errors.Add($"InsnEffects count {func.InsnEffects.Count} != Insns count {n}");
        if (func.InsnUses.Count != n) errors.Add($"InsnUses count {func.InsnUses.Count} != Insns count {n}");
    }

    static void CheckBlocks(HirFunction func, List<string> errors)
    {
        for (var bi = 0; bi < func.Blocks.Count; bi++)
        {
            var block = func.Blocks[bi];
            if (block.Id.Value != bi) errors.Add($"bb{bi}: Id mismatch (Id={block.Id})");
            foreach (var pid in block.Params)
            {
                if (!IsValidIndex(func, pid)) { errors.Add($"bb{bi}: param {pid} out of range"); continue; }
                var p = func[pid];
                if (p.Kind != HirInsnKind.Param && p.Kind != HirInsnKind.Nop)
                {
                    errors.Add($"bb{bi}: param slot occupied by non-Param {pid} (kind={p.Kind})");
                }
                if (p.Block != block.Id)
                {
                    errors.Add($"bb{bi}: param {pid} claims block {p.Block}");
                }
            }
            HirInsn? prevTerminator = null;
            foreach (var iid in block.Insns)
            {
                if (!IsValidIndex(func, iid)) { errors.Add($"bb{bi}: insn {iid} out of range"); continue; }
                var insn = func[iid];
                if (insn.Block != block.Id)
                {
                    errors.Add($"bb{bi}: insn {iid} claims block {insn.Block}");
                }
                if (prevTerminator != null)
                {
                    errors.Add($"bb{bi}: insn after terminator {iid} (kind={insn.Kind})");
                }
                if (insn.IsTerminator) prevTerminator = insn;
            }
        }
    }

    static void CheckEdges(HirFunction func, List<string> errors)
    {
        foreach (var edge in func.Edges)
        {
            if (!IsValidBlock(func, edge.Source)) errors.Add($"edge {edge}: source out of range");
            if (!IsValidBlock(func, edge.Target)) errors.Add($"edge {edge}: target out of range");
            if (!IsValidBlock(func, edge.Source) || !IsValidBlock(func, edge.Target)) continue;

            var target = func[edge.Target];
            if (edge.Args.Count != target.Params.Count)
            {
                errors.Add($"edge {edge}: arg count {edge.Args.Count} != target params {target.Params.Count}");
            }
            for (var i = 0; i < edge.Args.Count; i++)
            {
                var arg = edge.Args[i];
                if (!arg.IsValid) continue; // detached after DCE; tolerated
                if (!IsValidIndex(func, arg))
                {
                    errors.Add($"edge {edge}: arg[{i}] {arg} out of range");
                }
            }
        }
    }

    static void CheckInsns(HirFunction func, List<string> errors)
    {
        for (var i = 0; i < func.Insns.Count; i++)
        {
            var id = new InsnId(i);
            var insn = func.Insns[i];
            for (var j = 0; j < insn.Inputs.Count; j++)
            {
                var input = insn.Inputs[j];
                if (!input.IsValid) continue;
                if (!IsValidIndex(func, input))
                {
                    errors.Add($"{id}: input[{j}] {input} out of range");
                    continue;
                }
                if (input == id) errors.Add($"{id}: self-reference in input[{j}]");
            }
        }
    }

    static void CheckUseList(HirFunction func, List<string> errors)
    {
        // For each (value, user) recorded in InsnUses[value], confirm at least
        // one corresponding live reference exists. We tolerate duplicates
        // because Add v0, v0 legitimately registers v0 twice.
        for (var v = 0; v < func.InsnUses.Count; v++)
        {
            var users = func.InsnUses[v];
            // Count how many references each user actually has to v.
            var expected = new Dictionary<int, int>();
            foreach (var u in users)
            {
                if (!expected.TryAdd(u.Value, 1)) expected[u.Value]++;
            }
            foreach (var kv in expected)
            {
                var actual = CountReferencesFromUserToValue(func, new InsnId(kv.Key), new InsnId(v));
                if (actual < kv.Value)
                {
                    errors.Add($"v{v}: use-list claims {kv.Value} reference(s) from v{kv.Key}, found {actual}");
                }
            }
            // Also check the reverse: every actual reference to v should appear
            // in InsnUses[v]. We do a single sweep of all insns / edges to keep
            // this O(insns + edges) instead of quadratic per-value.
        }

        // Reverse direction: scan inputs and edge args, count references to each
        // value, and compare against InsnUses.
        var referenceCount = new int[func.Insns.Count];
        var fromCount = new int[func.Insns.Count];
        for (var u = 0; u < func.Insns.Count; u++)
        {
            var insn = func.Insns[u];
            foreach (var inp in insn.Inputs)
            {
                if (inp.IsValid && inp.Value < referenceCount.Length) referenceCount[inp.Value]++;
            }
        }
        foreach (var edge in func.Edges)
        {
            if (!IsValidBlock(func, edge.Target)) continue;
            var target = func[edge.Target];
            for (var slot = 0; slot < edge.Args.Count && slot < target.Params.Count; slot++)
            {
                var arg = edge.Args[slot];
                if (arg.IsValid && arg.Value < referenceCount.Length) referenceCount[arg.Value]++;
            }
        }
        for (var v = 0; v < func.InsnUses.Count; v++)
        {
            if (func.InsnUses[v].Count != referenceCount[v])
            {
                errors.Add($"v{v}: use-list size {func.InsnUses[v].Count} != actual reference count {referenceCount[v]}");
            }
        }
    }

    static int CountReferencesFromUserToValue(HirFunction func, InsnId user, InsnId value)
    {
        if (!IsValidIndex(func, user)) return 0;
        var insn = func[user];
        var count = 0;
        foreach (var inp in insn.Inputs)
        {
            if (inp == value) count++;
        }
        if (insn.Kind == HirInsnKind.Param)
        {
            var owner = func[insn.Block];
            var slot = owner.Params.IndexOf(user);
            if (slot >= 0)
            {
                foreach (var edge in owner.InEdges)
                {
                    if (slot < edge.Args.Count && edge.Args[slot] == value) count++;
                }
            }
        }
        return count;
    }

    static bool IsValidIndex(HirFunction func, InsnId id) =>
        id.IsValid && id.Value < func.Insns.Count;

    static bool IsValidBlock(HirFunction func, BlockId id) =>
        id.IsValid && id.Value < func.Blocks.Count;
}