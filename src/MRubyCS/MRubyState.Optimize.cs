using MRubyCS.Hir;
using MRubyCS.Hir.Lowering;
using MRubyCS.Hir.Passes;

namespace MRubyCS;

partial class MRubyState
{
    OptimizationInvariants? optimizationInvariants;

    /// <summary>
    /// Lazily-allocated registry of compile-time assumptions made by
    /// MRubyState.Optimize. Phase J will hook into VM mutation points
    /// (DefineMethod / const_set / class reopen) to invalidate dependent
    /// optimized Ireps; for now it's record-only.
    /// </summary>
    public OptimizationInvariants OptimizationInvariants =>
        optimizationInvariants ??= new OptimizationInvariants();

    /// <summary>
    /// Run the HIR optimization pipeline over <paramref name="src"/> and
    /// return a new <see cref="Irep"/> whose <c>Sequence</c> is the lowered
    /// bytecode. Children are optimized recursively first, then this Irep.
    ///
    /// Falls back to the original bytecode (with optimized children) when
    /// lowering encounters an unsupported insn kind — this happens at the
    /// toplevel of typical programs (TDef / Class / Method / Def aren't
    /// lifted yet) and is intentional during the M2 buildout.
    ///
    /// Catch handlers are also a fallback trigger: their PC ranges reference
    /// the original sequence, so any Irep with handlers stays as-is until the
    /// emitter learns to rewrite them.
    /// </summary>
    public Irep Optimize(Irep src)
    {
        var optimizedChildren = OptimizeChildren(src);

        if (src.CatchHandlers.Length > 0)
        {
            // Punt: catch handlers reference original PCs. Rewriting them
            // requires per-insn PC tracking through the emitter (deferred).
            return WithChildren(src, optimizedChildren);
        }

        try
        {
            var func = HirFunction.Build(src);
            TypeInference.Run(func);
            ConstantResolution.Run(func, this);
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
                Children = optimizedChildren,
                CatchHandlers = src.CatchHandlers,
            };
        }
        catch (System.NotSupportedException)
        {
            // Lowering hit an insn kind it doesn't yet handle. Keep the
            // original bytecode for this Irep but propagate the (possibly
            // optimized) children.
            return WithChildren(src, optimizedChildren);
        }
    }

    Irep[] OptimizeChildren(Irep src)
    {
        if (src.Children.Length == 0) return src.Children;
        var result = new Irep[src.Children.Length];
        var anyChanged = false;
        for (var i = 0; i < src.Children.Length; i++)
        {
            var child = src.Children[i];
            var optChild = Optimize(child);
            if (!ReferenceEquals(optChild, child)) anyChanged = true;
            result[i] = optChild;
        }
        return anyChanged ? result : src.Children;
    }

    static Irep WithChildren(Irep src, Irep[] children)
    {
        if (ReferenceEquals(children, src.Children)) return src;
        return new Irep
        {
            Flags = src.Flags,
            RegisterVariableCount = src.RegisterVariableCount,
            Sequence = src.Sequence,
            Symbols = src.Symbols,
            LocalVariables = src.LocalVariables,
            PoolValues = src.PoolValues,
            Children = children,
            CatchHandlers = src.CatchHandlers,
        };
    }
}
