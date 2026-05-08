namespace MRubyCS.Hir.Passes;

// Simplify φ-merges in blocks with exactly one predecessor. For such a block,
// every Param's value is unambiguously the corresponding edge.Args[slot] —
// the union over a singleton set is the singleton itself. We can therefore
// replace every use of the Param with the incoming arg, after which DCE can
// retire the Param.
//
// This corresponds to ZJIT's clean_cfg behavior of collapsing trivial phis.
// For single-predecessor blocks it's an unconditional win:
//
//   bb1:  ; preds=bb0
//     Return v9       ; v9 = bb1.Params[1], the edge from bb0 carries v1 here
//
//   →
//
//   bb1:  ; preds=bb0
//     Return v1
//
// Multi-predecessor blocks are left alone — collapsing those requires that
// every incoming arg be the same SSA value, which is a stronger condition
// best handled together with block merging.
public static class PhiSimplify
{
    public static int Run(HirFunction func)
    {
        var rewritten = 0;
        foreach (var block in func.Blocks)
        {
            if (block.InEdges.Count != 1) continue;
            var edge = block.InEdges[0];
            for (var i = 0; i < block.Params.Count && i < edge.Args.Count; i++)
            {
                var paramId = block.Params[i];
                var paramInsn = func[paramId];
                if (paramInsn.Kind != HirInsnKind.Param) continue;
                var arg = edge.Args[i];
                if (!arg.IsValid) continue;
                if (arg == paramId) continue;
                func.MakeEqualTo(paramId, arg);
                rewritten++;
            }
        }
        return rewritten;
    }
}
