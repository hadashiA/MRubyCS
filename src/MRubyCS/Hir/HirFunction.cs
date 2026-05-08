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

    // Reverse adjacency: InsnUses[v] is the multiset of insns that reference v.
    // - If v appears in U.Inputs, U is in InsnUses[v.Value] once per occurrence.
    // - If v is an edge.Args[i] flowing into Block.Params[i] = p, then p is
    //   in InsnUses[v.Value] once per such edge.
    // This duplication is intentional so that AddUse/RemoveUse stay symmetric
    // and a value's reference count == InsnUses[v].Count.
    public List<List<InsnId>> InsnUses { get; } = [];

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

    // Register `arg` as flowing into the i-th param of the edge's target.
    // Maintains InsnUses so the param participates in arg's use count.
    internal void AppendEdgeArg(HirBranchEdge edge, InsnId arg, int paramSlot)
    {
        edge.Args.Add(arg);
        var target = Blocks[edge.Target.Value];
        if ((uint)paramSlot < (uint)target.Params.Count && arg.IsValid)
        {
            AddUse(arg, target.Params[paramSlot]);
        }
    }

    internal InsnId Push(BlockId block, HirInsn insn)
    {
        var id = new InsnId(Insns.Count);
        insn.Block = block;
        Insns.Add(insn);
        InsnTypes.Add(HirType.Empty);
        InsnEffects.Add(HirEffect.None);
        InsnUses.Add(new List<InsnId>());
        Blocks[block.Value].Insns.Add(id);
        foreach (var input in insn.Inputs)
        {
            if (input.IsValid) AddUse(input, id);
        }
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
        InsnUses.Add(new List<InsnId>());
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

    public IReadOnlyList<InsnId> UsesOf(InsnId id) => InsnUses[id.Value];

    // --- Use-list maintenance ---

    void AddUse(InsnId value, InsnId user)
    {
        InsnUses[value.Value].Add(user);
    }

    // Remove one occurrence of `user` from value's use list. Symmetric with
    // AddUse: if a user references `value` twice (e.g. Add v0, v0), both entries
    // exist and are removed individually as inputs are rewritten.
    void RemoveOneUse(InsnId value, InsnId user)
    {
        var list = InsnUses[value.Value];
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] == user)
            {
                list.RemoveAt(i);
                return;
            }
        }
    }

    // Replace every reference to `from` with `to`. Walks all current users of
    // `from` (instruction inputs and edge args targeting params), rewrites them,
    // and migrates the use-list entries. After this returns, InsnUses[from] is
    // empty and `from` is dead unless something else keeps it live.
    public void MakeEqualTo(InsnId from, InsnId to)
    {
        if (from == to) return;
        var users = InsnUses[from.Value];
        // Snapshot: rewriting may cause re-entrant modifications.
        var snapshot = users.ToArray();
        users.Clear();
        foreach (var user in snapshot)
        {
            RewriteUserReferences(user, from, to);
        }
    }

    void RewriteUserReferences(InsnId user, InsnId from, InsnId to)
    {
        // Direct Inputs of the user-as-instruction.
        var insn = Insns[user.Value];
        var inputs = insn.Inputs;
        for (var i = 0; i < inputs.Count; i++)
        {
            if (inputs[i] == from)
            {
                inputs[i] = to;
                if (to.IsValid) AddUse(to, user);
            }
        }
        // If `user` is a Param, every incoming edge.Args entry that targets it
        // needs rewriting too.
        if (insn.Kind == HirInsnKind.Param)
        {
            var owner = Blocks[insn.Block.Value];
            var slot = owner.Params.IndexOf(user);
            if (slot >= 0)
            {
                foreach (var edge in owner.InEdges)
                {
                    if (slot < edge.Args.Count && edge.Args[slot] == from)
                    {
                        edge.Args[slot] = to;
                        if (to.IsValid) AddUse(to, user);
                    }
                }
            }
        }
    }

    // Drop a single input/use linkage (used by DCE when a producer is being
    // deleted and we have to detach it cleanly from its consumers).
    internal void DetachInputs(InsnId user)
    {
        var insn = Insns[user.Value];
        foreach (var input in insn.Inputs)
        {
            if (input.IsValid) RemoveOneUse(input, user);
        }
        insn.Inputs.Clear();
        if (insn.Kind == HirInsnKind.Param)
        {
            var owner = Blocks[insn.Block.Value];
            var slot = owner.Params.IndexOf(user);
            if (slot >= 0)
            {
                foreach (var edge in owner.InEdges)
                {
                    if (slot < edge.Args.Count)
                    {
                        var arg = edge.Args[slot];
                        if (arg.IsValid) RemoveOneUse(arg, user);
                        edge.Args[slot] = InsnId.Invalid;
                    }
                }
            }
        }
    }

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
