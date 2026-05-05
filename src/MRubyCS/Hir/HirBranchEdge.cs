using System.Collections.Generic;

namespace MRubyCS.Hir;

// A directed edge between two basic blocks. Edges carry the "argument values"
// for the target block's parameters — this is how SSA φ-merges are encoded
// without needing a separate φ instruction. Inspired by ZJIT BranchEdge.
public sealed class HirBranchEdge
{
    public BlockId Source { get; internal init; } = BlockId.Invalid;
    public BlockId Target { get; internal init; } = BlockId.Invalid;
    // One arg per Target.Params, in matching order. Arg type ⊑ Param type after
    // type inference.
    public List<InsnId> Args { get; } = [];

    internal HirBranchEdge() {}

    public override string ToString() => $"{Source} -> {Target} [{Args.Count} args]";
}
