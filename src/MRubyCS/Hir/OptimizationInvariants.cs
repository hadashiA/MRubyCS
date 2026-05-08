using System.Collections.Generic;

namespace MRubyCS.Hir;

// Compile-time assumptions made during MRubyState.Optimize that the runtime
// must keep stable for the optimized bytecode to remain semantically correct.
//
// Mirrors ZJIT's `Invariant` / `PatchPoint` registry but scoped down for an
// AOT-load-time setting:
//   * No runtime guards. Every assumption is "static and assumed stable".
//   * Invalidation is by re-doing optimization (or reverting to source).
//
// For Phase C-1 we record the assumptions but don't yet wire them to VM
// hooks — that's Phase J. The data structure is here so subsequent passes
// (method resolution, inlining) can build on it without churn.
public sealed class OptimizationInvariants
{
    /// <summary>Constants that resolved to specific classes during Optimize.</summary>
    public Dictionary<Symbol, RClass> ConstantBindings { get; } = new();

    /// <summary>
    /// Method dispatch decisions (class, method-name) -> resolved Irep.
    /// Reserved for Phase C-2; populated when Send insns get statically
    /// resolved to a specific implementation.
    /// </summary>
    public Dictionary<(RClass Class, Symbol Method), Irep> MethodBindings { get; } = new();

    public void RecordConstantBinding(Symbol name, RClass cls)
    {
        // Last writer wins. If the program reopens a class, the latest
        // binding sticks; the older Optimize results are simply stale.
        // Phase J will track dependents and invalidate properly.
        ConstantBindings[name] = cls;
    }

    public bool TryGetConstantBinding(Symbol name, out RClass cls)
    {
        return ConstantBindings.TryGetValue(name, out cls!);
    }

    public void RecordMethodBinding(RClass cls, Symbol method, Irep irep)
    {
        MethodBindings[(cls, method)] = irep;
    }
}
