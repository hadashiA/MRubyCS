using MRubyCS.Hir;
using MRubyCS.Hir.Lowering;
using MRubyCS.Hir.Passes;

namespace MRubyCS;

partial class MRubyState
{
    /// <summary>
    /// Run the HIR optimization pipeline over <paramref name="src"/> and
    /// return a new <see cref="Irep"/> whose <c>Sequence</c> is the lowered
    /// bytecode. Symbols / pool / children / catch handlers are copied as-is
    /// for now (children optimization lands in a later phase).
    ///
    /// This is the entry point that connects HIR transformations to actual VM
    /// execution. <see cref="Execute(Irep)"/> on the returned Irep should
    /// produce the same observable result as on the input.
    /// </summary>
    public Irep Optimize(Irep src)
    {
        var func = HirFunction.Build(src);
        TypeInference.Run(func);
        MoveElim.Run(func);
        ConstantFold.Run(func);
        PhiSimplify.Run(func);
        Dce.Run(func);
        HirVerifier.Verify(func);

        var lowered = HirLowering.Lower(func);

        return new Irep
        {
            Flags = src.Flags,
            RegisterVariableCount = lowered.RegisterCount,
            Sequence = lowered.Sequence,
            Symbols = src.Symbols,
            LocalVariables = src.LocalVariables,
            PoolValues = src.PoolValues,
            // TODO(M2 follow-up): recursively optimize children once function-
            // level optimizations stabilize. For now passthrough.
            Children = src.Children,
            // TODO: rewrite catch handler PCs against the new sequence layout
            // when catch handlers are present. H1 asserts none.
            CatchHandlers = src.CatchHandlers,
        };
    }
}
