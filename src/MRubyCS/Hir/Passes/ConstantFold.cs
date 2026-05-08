namespace MRubyCS.Hir.Passes;

// Compile-time constant folding for arithmetic and comparison insns whose
// inputs are all statically-known integer constants.
//
// Scope (intentionally narrow for v1):
//   - Add / Sub / Mul (binary) on ConstInt × ConstInt
//   - AddI / SubI on ConstInt × i32 immediate
//   - Eq / Lt / Le / Gt / Ge on ConstInt × ConstInt
//
// Out of scope (later):
//   - Float folding (HirType doesn't currently capture ConstFloat through
//     arithmetic — arithmetic on Float drops the spec).
//   - String concat folding.
//   - Branch direction simplification (BranchIf on a known boolean) — that
//     belongs to CleanCfg because it changes the CFG.
//   - Folding when result overflows i32. mruby integers can be 64-bit, but
//     LoadInt's payload (Aux1) is i32 — anything wider is left alone.
//
// Implementation note:
//   We mutate the insn in place (Kind/Aux/Inputs) rather than allocating a
//   new InsnId. Existing users of the SSA value continue to work because
//   their stored InsnId still resolves to the same slot — now holding a
//   LoadInt. This avoids the cost (and bookkeeping) of inserting a fresh
//   insn at the right CFG position.
public static class ConstantFold
{
    public static int Run(HirFunction func)
    {
        var folded = 0;
        for (var i = 0; i < func.Insns.Count; i++)
        {
            var id = new InsnId(i);
            var insn = func[id];
            if (TryFold(func, id, insn)) folded++;
        }
        return folded;
    }

    static bool TryFold(HirFunction func, InsnId id, HirInsn insn) => insn.Kind switch
    {
        HirInsnKind.Add or HirInsnKind.Sub or HirInsnKind.Mul =>
            TryFoldBinaryArith(func, id, insn),
        HirInsnKind.AddI or HirInsnKind.SubI =>
            TryFoldImmediateArith(func, id, insn),
        HirInsnKind.Eq or HirInsnKind.Lt or HirInsnKind.Le
            or HirInsnKind.Gt or HirInsnKind.Ge =>
            TryFoldComparison(func, id, insn),
        _ => false,
    };

    static bool TryFoldBinaryArith(HirFunction func, InsnId id, HirInsn insn)
    {
        if (insn.Inputs.Count != 2) return false;
        var lhs = func.TypeOf(insn.Inputs[0]);
        var rhs = func.TypeOf(insn.Inputs[1]);
        if (lhs.Spec != HirSpec.ConstInt || rhs.Spec != HirSpec.ConstInt) return false;
        long result;
        try
        {
            checked
            {
                result = insn.Kind switch
                {
                    HirInsnKind.Add => lhs.IntValue + rhs.IntValue,
                    HirInsnKind.Sub => lhs.IntValue - rhs.IntValue,
                    HirInsnKind.Mul => lhs.IntValue * rhs.IntValue,
                    _ => 0L,
                };
            }
        }
        catch (System.OverflowException) { return false; }
        if (result < int.MinValue || result > int.MaxValue) return false;
        ConvertToLoadInt(func, id, insn, (int)result);
        return true;
    }

    static bool TryFoldImmediateArith(HirFunction func, InsnId id, HirInsn insn)
    {
        if (insn.Inputs.Count != 1) return false;
        var lhs = func.TypeOf(insn.Inputs[0]);
        if (lhs.Spec != HirSpec.ConstInt) return false;
        long result;
        try
        {
            checked
            {
                result = insn.Kind == HirInsnKind.AddI
                    ? lhs.IntValue + insn.Aux1
                    : lhs.IntValue - insn.Aux1;
            }
        }
        catch (System.OverflowException) { return false; }
        if (result < int.MinValue || result > int.MaxValue) return false;
        ConvertToLoadInt(func, id, insn, (int)result);
        return true;
    }

    static bool TryFoldComparison(HirFunction func, InsnId id, HirInsn insn)
    {
        if (insn.Inputs.Count != 2) return false;
        var lhs = func.TypeOf(insn.Inputs[0]);
        var rhs = func.TypeOf(insn.Inputs[1]);
        if (lhs.Spec != HirSpec.ConstInt || rhs.Spec != HirSpec.ConstInt) return false;
        var l = lhs.IntValue;
        var r = rhs.IntValue;
        var result = insn.Kind switch
        {
            HirInsnKind.Eq => l == r,
            HirInsnKind.Lt => l < r,
            HirInsnKind.Le => l <= r,
            HirInsnKind.Gt => l > r,
            HirInsnKind.Ge => l >= r,
            _ => false,
        };
        ConvertToBool(func, id, insn, result);
        return true;
    }

    static void ConvertToLoadInt(HirFunction func, InsnId id, HirInsn insn, int value)
    {
        func.DetachInputs(id);
        ResetAux(insn);
        insn.Kind = HirInsnKind.LoadInt;
        insn.Aux1 = value;
        func.SetType(id, HirType.ConstInt(value));
        func.SetEffect(id, HirEffect.Pure);
    }

    static void ConvertToBool(HirFunction func, InsnId id, HirInsn insn, bool value)
    {
        func.DetachInputs(id);
        ResetAux(insn);
        insn.Kind = value ? HirInsnKind.LoadTrue : HirInsnKind.LoadFalse;
        func.SetType(id, value ? HirType.True : HirType.False);
        func.SetEffect(id, HirEffect.Pure);
    }

    static void ResetAux(HirInsn insn)
    {
        insn.Aux1 = 0;
        insn.Aux2 = 0;
        insn.AuxSymbol = default;
        insn.AuxBool = false;
        insn.AuxBool2 = false;
        insn.AuxObj = null;
    }
}
