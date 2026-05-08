using System.Collections.Generic;

namespace MRubyCS.Hir;

// One HIR instruction. Stored by index in HirFunction.Insns. References to
// other insns are by InsnId. Operand layout depends on Kind (see comments on
// HirInsnKind). Mutated by passes via HirFunction helpers.
public sealed class HirInsn
{
    public HirInsnKind Kind { get; internal set; }
    public BlockId Block { get; internal set; } = BlockId.Invalid;

    // Source bytecode for diagnostics and back-mapping when lowering.
    public int SourcePc { get; init; }
    public OpCode SourceOpCode { get; init; }

    // Inputs are SSA edges. NewArray/Hash/Send have variable lengths; everyone
    // else has 0..3. Stored as a List for uniformity; cost is one heap object
    // per insn but that mirrors ZJIT's Vec<InsnId> per Insn variant.
    public List<InsnId> Inputs { get; } = new();

    // Auxiliary scalar / object operands. Interpretation depends on Kind.
    // Internal-set so optimization passes can rewrite an insn in place
    // (e.g. fold Add → LoadInt) without invalidating users that already
    // hold the InsnId.
    public int Aux1 { get; internal set; }
    public int Aux2 { get; internal set; }
    public Symbol AuxSymbol { get; internal set; }
    public bool AuxBool { get; internal set; }
    public bool AuxBool2 { get; internal set; }
    public object? AuxObj { get; internal set; }

    internal HirInsn(HirInsnKind kind, int sourcePc, OpCode sourceOpCode)
    {
        Kind = kind;
        SourcePc = sourcePc;
        SourceOpCode = sourceOpCode;
    }

    // Convenience for passes: does this insn produce a value (have an SSA id
    // observed by other insns)? Param, LoadX, arithmetic, allocation, Send,
    // Get* all produce; Set*, branches, returns, jumps don't.
    public bool HasOutput => Kind switch
    {
        HirInsnKind.Jump => false,
        HirInsnKind.BranchIf or HirInsnKind.BranchUnless or HirInsnKind.BranchNil => false,
        HirInsnKind.Return or HirInsnKind.ReturnBlk or HirInsnKind.Break or HirInsnKind.Stop => false,
        HirInsnKind.RaiseIf => false,
        HirInsnKind.SetGV or HirInsnKind.SetIV or HirInsnKind.SetCV or HirInsnKind.SetSV
            or HirInsnKind.SetConst or HirInsnKind.SetMCnst or HirInsnKind.SetUpVar => false,
        HirInsnKind.SetIdx or HirInsnKind.ASet => false,
        HirInsnKind.Enter or HirInsnKind.Nop or HirInsnKind.Unknown or HirInsnKind.Other => false,
        _ => true,
    };

    public bool IsTerminator => Kind switch
    {
        HirInsnKind.Jump or HirInsnKind.BranchIf or HirInsnKind.BranchUnless or HirInsnKind.BranchNil
            or HirInsnKind.Return or HirInsnKind.ReturnBlk or HirInsnKind.Break or HirInsnKind.Stop => true,
        _ => false,
    };
}
