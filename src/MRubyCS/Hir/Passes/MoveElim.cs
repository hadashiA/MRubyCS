namespace MRubyCS.Hir.Passes;

// Strip Move insns introduced by the lifter. The original mruby OP_Move
// generates a fresh SSA value identical to its input; in SSA form the
// distinction is meaningless. Replacing every use of `Move v` with `v`
// itself folds the slot away. DCE then deletes the now-unused Move.
//
// This pass leaves Move instructions whose input is invalid alone (defensive,
// shouldn't happen in well-formed HIR but cheap to guard against).
public static class MoveElim
{
    public static int Run(HirFunction func)
    {
        var rewritten = 0;
        for (var i = 0; i < func.Insns.Count; i++)
        {
            var id = new InsnId(i);
            var insn = func[id];
            if (insn.Kind != HirInsnKind.Move) continue;
            if (insn.Inputs.Count != 1) continue;
            var src = insn.Inputs[0];
            if (!src.IsValid) continue;
            func.MakeEqualTo(id, src);
            rewritten++;
        }
        return rewritten;
    }
}