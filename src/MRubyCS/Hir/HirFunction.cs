using System.Collections.Generic;

namespace MRubyCS.Hir;

// One HIR per Irep. Owns three flat arenas:
//   Insns      : List<HirInsn>      — keyed by InsnId.Value
//   InsnTypes  : List<HirType>      — parallel; updated by type inference
//   InsnEffects: List<HirEffect>    — parallel; updated by effect analysis
//   Blocks     : List<HirBlock>     — keyed by BlockId.Value
//
// Plus a flat list of edges (so passes can iterate them without traversing
// blocks). Each block also keeps its in/out edge references, but the edges
// themselves live here.
public sealed class HirFunction
{
    public Irep SourceIrep { get; }
    public int RegisterCount { get; }

    public List<HirInsn> Insns { get; } = [];
    public List<HirType> InsnTypes { get; } = [];
    public List<HirEffect> InsnEffects { get; } = [];

    public List<HirBlock> Blocks { get; } = [];
    public List<HirBranchEdge> Edges { get; } = [];

    public BlockId EntryBlock { get; internal set; } = BlockId.Invalid;

    internal HirFunction(Irep irep)
    {
        SourceIrep = irep;
        RegisterCount = irep.RegisterVariableCount;
    }

    // --- Allocators ---

    internal HirBlock NewBlock(int startPc, int endPc)
    {
        var b = new HirBlock { Id = new BlockId(Blocks.Count), StartPc = startPc, EndPc = endPc };
        Blocks.Add(b);
        return b;
    }

    internal HirBranchEdge NewEdge(BlockId src, BlockId dst)
    {
        var e = new HirBranchEdge { Source = src, Target = dst };
        Edges.Add(e);
        Blocks[src.Value].OutEdges.Add(e);
        Blocks[dst.Value].InEdges.Add(e);
        return e;
    }

    internal InsnId Push(BlockId block, HirInsn insn)
    {
        var id = new InsnId(Insns.Count);
        insn.Block = block;
        Insns.Add(insn);
        InsnTypes.Add(HirType.Empty);
        InsnEffects.Add(HirEffect.None);
        Blocks[block.Value].Insns.Add(id);
        return id;
    }

    // Block params are insns of kind Param; they live "before" any other insn
    // in the block but are stored separately in Block.Params (not in Insns
    // list of the block — they're conceptually the block's incoming values).
    internal InsnId PushParam(BlockId block, int slotIdx)
    {
        var insn = new HirInsn(HirInsnKind.Param, sourcePc: -1, sourceOpCode: 0)
        {
            Aux1 = slotIdx,
        };
        var id = new InsnId(Insns.Count);
        insn.Block = block;
        Insns.Add(insn);
        InsnTypes.Add(HirType.Empty);
        InsnEffects.Add(HirEffect.None);
        Blocks[block.Value].Params.Add(id);
        return id;
    }

    // --- Accessors ---

    public HirInsn this[InsnId id] => Insns[id.Value];
    public HirBlock this[BlockId id] => Blocks[id.Value];

    public HirType TypeOf(InsnId id) => InsnTypes[id.Value];
    internal void SetType(InsnId id, HirType ty) => InsnTypes[id.Value] = ty;

    public HirEffect EffectOf(InsnId id) => InsnEffects[id.Value];
    internal void SetEffect(InsnId id, HirEffect e) => InsnEffects[id.Value] = e;

    // Reverse postorder (DFS-based) over reachable blocks from entry. Used by
    // type inference and most analyses that want a topo-ish walk.
    public List<BlockId> ReversePostOrder()
    {
        var result = new List<BlockId>(Blocks.Count);
        var visited = new bool[Blocks.Count];
        if (EntryBlock.IsValid)
        {
            DfsPostOrder(EntryBlock, visited, result);
            result.Reverse();
        }
        return result;
    }

    void DfsPostOrder(BlockId b, bool[] visited, List<BlockId> output)
    {
        if (visited[b.Value]) return;
        visited[b.Value] = true;
        foreach (var edge in Blocks[b.Value].OutEdges)
        {
            DfsPostOrder(edge.Target, visited, output);
        }
        output.Add(b);
    }

    public static HirFunction Build(Irep irep) => HirBuilder.Build(irep);
}
