namespace MRubyCS.Hir.Passes;

// Iterative fixed-point type inference, ZJIT-style. Walks all blocks in
// reverse-postorder, computes each insn's output type from inputs + kind,
// unions arg types into target params on every edge. Repeats until no
// type changed in a full pass.
//
// Soundness contract:
//   - Initial type for every insn is HirType.Empty (bottom).
//   - Param types are widened by Union of incoming edge args.
//   - Non-param insns get their type from InferKindType().
//   - Convergence: union and the per-kind transfer functions are monotone, and
//     the lattice has finite height (Empty < specific bits < Any), so the
//     iteration terminates.
public static class TypeInference
{
    public static void Run(HirFunction func)
    {
        // Reset all types so reruns after CFG-changing passes start fresh.
        for (var i = 0; i < func.InsnTypes.Count; i++) func.InsnTypes[i] = HirType.Empty;

        // The entry block's params don't have incoming edges. They represent
        // the function's argument values; for now, treat them as Any.
        if (func.EntryBlock.IsValid)
        {
            var entry = func[func.EntryBlock];
            foreach (var p in entry.Params) func.SetType(p, HirType.Any);
        }

        var rpo = func.ReversePostOrder();
        bool changed;
        do
        {
            changed = false;
            foreach (var blockId in rpo)
            {
                var block = func[blockId];

                // Step 1: union incoming edge args into params.
                foreach (var edge in block.InEdges)
                {
                    for (var i = 0; i < edge.Args.Count && i < block.Params.Count; i++)
                    {
                        var arg = edge.Args[i];
                        var param = block.Params[i];
                        var argType = func.TypeOf(arg);
                        var newParamType = func.TypeOf(param).Union(argType);
                        if (!newParamType.Equals(func.TypeOf(param)))
                        {
                            func.SetType(param, newParamType);
                            changed = true;
                        }
                    }
                }

                // Step 2: transfer functions for each insn in block order.
                foreach (var id in block.Insns)
                {
                    var insn = func[id];
                    if (!insn.HasOutput) continue;
                    var newType = InferKindType(func, insn);
                    if (!newType.Equals(func.TypeOf(id)))
                    {
                        func.SetType(id, newType);
                        changed = true;
                    }
                }
            }
        } while (changed);

        // Effects are static per-kind for now; populate alongside.
        for (var i = 0; i < func.Insns.Count; i++)
        {
            func.SetEffect(new InsnId(i), HirEffect.ForKind(func.Insns[i].Kind));
        }
    }

    static HirType InferKindType(HirFunction func, HirInsn insn) => insn.Kind switch
    {
        HirInsnKind.LoadNil => HirType.Nil,
        HirInsnKind.LoadTrue => HirType.True,
        HirInsnKind.LoadFalse => HirType.False,
        HirInsnKind.LoadInt => HirType.ConstInt(insn.Aux1),
        HirInsnKind.LoadSym => HirType.ConstSym(insn.AuxSymbol),
        HirInsnKind.LoadSelf => HirType.Any,    // could pin to enclosing class later
        HirInsnKind.LoadPool => InferPool(insn),
        HirInsnKind.NewArray => HirType.Array,
        HirInsnKind.NewHash => HirType.Hash,
        HirInsnKind.NewString => HirType.String,
        HirInsnKind.NewRange => HirType.Range,
        HirInsnKind.Lambda or HirInsnKind.Block or HirInsnKind.Method => HirType.Proc,
        HirInsnKind.Move => insn.Inputs.Count > 0 ? func.TypeOf(insn.Inputs[0]) : HirType.Any,
        HirInsnKind.AddI or HirInsnKind.SubI =>
            // If LHS is provably Integer, result is too (mod overflow). If LHS
            // is Float, we still produce Numeric. Otherwise Any.
            ArithmeticResult(func.TypeOf(insn.Inputs[0]), HirType.Integer),
        HirInsnKind.Add or HirInsnKind.Sub or HirInsnKind.Mul or HirInsnKind.Div =>
            ArithmeticResult(func.TypeOf(insn.Inputs[0]), func.TypeOf(insn.Inputs[1])),
        HirInsnKind.Eq or HirInsnKind.Lt or HirInsnKind.Le or HirInsnKind.Gt or HirInsnKind.Ge =>
            HirType.Bool,
        HirInsnKind.Send or HirInsnKind.Super or HirInsnKind.Call => HirType.Any,
        HirInsnKind.GetIV or HirInsnKind.GetGV or HirInsnKind.GetCV
            or HirInsnKind.GetConst or HirInsnKind.GetMCnst
            or HirInsnKind.GetUpVar => HirType.Any,
        HirInsnKind.GetIdx or HirInsnKind.ARef => HirType.Any,
        HirInsnKind.Param => HirType.Any,    // overwritten by edge-arg merging
        _ => HirType.Any,
    };

    static HirType InferPool(HirInsn insn)
    {
        if (insn.AuxObj is MRubyValue mv)
        {
            return mv.VType switch
            {
                MRubyVType.Integer => HirType.Integer,
                MRubyVType.Float => HirType.Float,
                MRubyVType.String => HirType.String,
                MRubyVType.Symbol => HirType.Symbol,
                _ => HirType.Any,
            };
        }
        return HirType.Any;
    }

    static HirType ArithmeticResult(HirType lhs, HirType rhs)
    {
        // Both Integer => Integer (mod overflow promotion in mruby; for v1 we
        // call it Integer and refine when we add overflow-aware lattice).
        if (lhs.Bits == HirTypeBits.Integer && rhs.Bits == HirTypeBits.Integer)
            return HirType.Integer;
        // Either Float, the other Numeric => Float.
        if ((lhs.Bits & HirTypeBits.Float) != 0 || (rhs.Bits & HirTypeBits.Float) != 0)
        {
            if ((lhs.Bits & ~HirTypeBits.Numeric) == 0 && (rhs.Bits & ~HirTypeBits.Numeric) == 0)
                return HirType.Float;
        }
        // Both Numeric (integer or float) => Numeric.
        if ((lhs.Bits & ~HirTypeBits.Numeric) == 0 && (rhs.Bits & ~HirTypeBits.Numeric) == 0
            && lhs.Bits != HirTypeBits.None && rhs.Bits != HirTypeBits.None)
            return new HirType().Union(HirType.Integer).Union(HirType.Float);
        // Otherwise Any (could call user-defined +, etc.).
        return HirType.Any;
    }
}
