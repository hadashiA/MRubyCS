namespace MRubyCS.Hir.Passes;

// Pure dead-code elimination. Removes value-producing insns whose result has
// no users and which have no observable effect.
//
// Phase scope (A-3): only Pure insns are deletable. Allocations stay live
// even if unused — Phase E (allocation elision) handles those once escape
// analysis can prove the heap pressure is unobservable.
//
// Param insns are special: they are positional (their index in Block.Params
// must match edge.Args[slot]). Rather than physically removing a dead Param,
// we mark it as Nop and detach its edge-arg uses. The slot stays in
// Block.Params; downstream passes can compact later.
public static class Dce
{
    /// <summary>Run DCE to fixed point. Returns the number of insns removed.</summary>
    public static int Run(HirFunction func)
    {
        var removed = 0;
        bool changed;
        do
        {
            changed = false;
            for (var i = 0; i < func.Insns.Count; i++)
            {
                var id = new InsnId(i);
                var insn = func[id];
                if (insn.Kind == HirInsnKind.Nop) continue;
                if (!IsRemovable(func, id, insn)) continue;
                Kill(func, id, insn);
                removed++;
                changed = true;
            }
        } while (changed);
        return removed;
    }

    static bool IsRemovable(HirFunction func, InsnId id, HirInsn insn)
    {
        if (!insn.HasOutput) return false;
        // Use the kind's static effect classification, not the per-insn cache,
        // so DCE works regardless of whether type inference has populated
        // refined effects.
        if (!HirEffect.ForKind(insn.Kind).IsPure) return false;
        return func.UsesOf(id).Count == 0;
    }

    static void Kill(HirFunction func, InsnId id, HirInsn insn)
    {
        func.DetachInputs(id);

        if (insn.Kind != HirInsnKind.Param)
        {
            // Physical removal from block list — Param positions are reserved
            // and turning the kind into Nop is enough.
            var block = func[insn.Block];
            block.Insns.Remove(id);
        }

        // Switch kind to Nop so the slot becomes inert. The instruction's
        // SourcePc/SourceOpCode are kept for debugging.
        insn.Kind = HirInsnKind.Nop;
    }
}