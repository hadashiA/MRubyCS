namespace MRubyCS.Hir.Passes;

// Phase C-1: refine GetConst SSA values to a specific class type when the
// state already has the constant bound at Optimize time.
//
// Without this pass, `GetConst :Vec` is typed as Any, which forces
// downstream Send insns through the receiver to stay Any -> Any. With it,
// the SSA value is `ClassObj=Vec` (or `ClassOrSubclassOf` once we widen),
// which lets later passes (method resolution, inlining) reason about the
// receiver class.
//
// Scope (intentional, M3-minimal):
//   * Only resolves GetConst, not GetMCnst (Foo::Bar). Module-scoped lookups
//     need scope tracking which we don't model in HIR yet.
//   * Only refines when the constant is already bound on the state. For an
//     AOT path where the script does `class Foo; end` itself before usage,
//     a future pass needs to either (a) statically interpret class
//     definitions in the toplevel Irep, or (b) defer optimization of code
//     that references not-yet-bound classes.
//   * Records into MRubyState.OptimizationInvariants so Phase J can later
//     wire VM mutation hooks to invalidate dependent optimized Ireps.
public static class ConstantResolution
{
    public static int Run(HirFunction func, MRubyState state)
    {
        var invariants = state.OptimizationInvariants;
        var refined = 0;
        for (var i = 0; i < func.Insns.Count; i++)
        {
            var insn = func.Insns[i];
            if (insn.Kind != HirInsnKind.GetConst) continue;
            var sym = insn.AuxSymbol;
            if (sym.Value == 0) continue;

            if (!state.TryGetConst(sym, out var value)) continue;
            if (value.Object is not RClass cls) continue;

            var bits = cls.VType switch
            {
                MRubyVType.Class or MRubyVType.SClass => HirTypeBits.ClassObj,
                MRubyVType.Module => HirTypeBits.ModuleObj,
                _ => HirTypeBits.ClassObj,
            };
            func.SetType(new InsnId(i), HirType.ExactClassOf(cls, bits));
            invariants.RecordConstantBinding(sym, cls);
            refined++;
        }
        return refined;
    }
}
