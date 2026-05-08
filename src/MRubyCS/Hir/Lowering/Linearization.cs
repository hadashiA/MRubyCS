using System.Collections.Generic;

namespace MRubyCS.Hir.Lowering;

// Flat linear ordering of every "program point" used by register allocation.
// Each block contributes:
//   * one slot for the block-start (Params are conceptually defined here)
//   * one slot per Insn in block.Insns
//   * one slot for the block-end (parallel-copies for outgoing edges happen
//     here, conceptually using the edge.Args values)
//
// Linear scan operates on this ordering: a value's live range is
// [defAt[value], lastUse[value]].
internal sealed class Linearization
{
    public Dictionary<int, int> DefAt { get; } = new();        // InsnId.Value -> linear pos
    public Dictionary<int, int> LastUse { get; } = new();       // InsnId.Value -> linear pos
    public Dictionary<int, int> BlockStart { get; } = new();    // BlockId.Value -> linear pos
    public Dictionary<int, int> BlockEnd { get; } = new();      // BlockId.Value -> linear pos
    public int Count { get; private set; }

    public static Linearization Compute(HirFunction func, BlockLayout layout)
    {
        var lin = new Linearization();
        var idx = 0;
        foreach (var blockId in layout.Order)
        {
            var block = func[blockId];
            lin.BlockStart[blockId.Value] = idx;
            foreach (var paramId in block.Params)
            {
                var p = func[paramId];
                if (p.Kind != HirInsnKind.Param) continue;
                lin.DefAt[paramId.Value] = idx;
            }
            idx++;

            foreach (var insnId in block.Insns)
            {
                var insn = func[insnId];
                if (insn.Kind == HirInsnKind.Nop) { idx++; continue; }
                if (insn.HasOutput) lin.DefAt[insnId.Value] = idx;
                foreach (var input in insn.Inputs)
                {
                    if (!input.IsValid) continue;
                    UpdateLastUse(lin, input.Value, idx);
                }
                idx++;
            }

            lin.BlockEnd[blockId.Value] = idx;
            foreach (var edge in block.OutEdges)
            {
                foreach (var arg in edge.Args)
                {
                    if (!arg.IsValid) continue;
                    UpdateLastUse(lin, arg.Value, idx);
                }
            }
            idx++;
        }
        lin.Count = idx;
        return lin;
    }

    static void UpdateLastUse(Linearization lin, int valueIdx, int pos)
    {
        if (lin.LastUse.TryGetValue(valueIdx, out var prev) && prev >= pos) return;
        lin.LastUse[valueIdx] = pos;
    }
}
