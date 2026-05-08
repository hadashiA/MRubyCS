using System.Collections.Generic;

namespace MRubyCS.Hir.Lowering;

// Linear ordering of basic blocks for emission. v1 just uses reverse postorder,
// which gives a topo-ish layout where every block precedes its dominator-tree
// successors. Later we may add fall-through-aware heuristics (place a block
// immediately after one of its predecessors when possible to skip a Jump).
internal sealed class BlockLayout
{
    public IReadOnlyList<BlockId> Order { get; }
    public IReadOnlyDictionary<BlockId, int> IndexOf { get; }

    BlockLayout(IReadOnlyList<BlockId> order)
    {
        Order = order;
        var map = new Dictionary<BlockId, int>(order.Count);
        for (var i = 0; i < order.Count; i++) map[order[i]] = i;
        IndexOf = map;
    }

    public static BlockLayout Compute(HirFunction func) =>
        new(func.ReversePostOrder());
}
