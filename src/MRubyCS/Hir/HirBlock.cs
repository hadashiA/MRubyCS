using System.Collections.Generic;

namespace MRubyCS.Hir;

// Basic block. Contains:
//   - Params: incoming SSA values (φ slots). Edges flowing in carry matching args.
//   - Insns: ordered list of instruction ids. Last is terminator (or empty).
//   - InEdges / OutEdges: control flow.
public sealed class HirBlock
{
    public BlockId Id { get; internal set; } = BlockId.Invalid;
    public int StartPc { get; internal set; }
    public int EndPc { get; internal set; }     // exclusive

    public List<InsnId> Params { get; } = [];
    public List<InsnId> Insns { get; } = [];
    public List<HirBranchEdge> InEdges { get; } = [];
    public List<HirBranchEdge> OutEdges { get; } = [];

    internal HirBlock() {}

    public override string ToString() => $"{Id} [{StartPc:x4}..{EndPc:x4})";
}
