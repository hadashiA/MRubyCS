using System;

namespace MRubyCS.Internals;

// Operand layout of each mruby 4.0 opcode (mruby/include/mruby/ops.h reference).
// Z=1, B=2, S=3, BB=3, BS=4, BBB=4, BSS=6, W=4 (bytes including the opcode itself).
internal enum OperandLayout : byte
{
    Z,
    B,
    S,
    BB,
    BS,
    BBB,
    BSS,
    W,
}

internal static class OpCodeInfo
{
    // Cached for hot lookups. Indexed by opcode byte value.
    static readonly OperandLayout[] layouts = BuildLayouts();
    static readonly byte[] widths = BuildWidths();

    public static OperandLayout Layout(OpCode op) => layouts[(byte)op];

    public static int Width(OpCode op) => widths[(byte)op];

    // PC of the byte after this instruction.
    public static int NextPc(OpCode op, int pc) => pc + widths[(byte)op];

    // Does this op end its basic block? (Unconditional jump, return, raise, stop.)
    public static bool IsTerminator(OpCode op) => op switch
    {
        OpCode.Jmp or OpCode.JmpUw or
        OpCode.Return or OpCode.ReturnBlk or
        OpCode.RetSelf or OpCode.RetNil or OpCode.RetTrue or OpCode.RetFalse or
        OpCode.Break or OpCode.RaiseIf or OpCode.Stop => true,
        _ => false,
    };

    // Conditional branches that fall through to the next instruction.
    public static bool IsBranch(OpCode op) => op switch
    {
        OpCode.JmpIf or OpCode.JmpNot or OpCode.JmpNil => true,
        _ => false,
    };

    // Returns true if the op transfers control to a relative jump target encoded in its
    // S-typed operand. Sets `targetPc` to the absolute target PC in the source bytecode.
    // Pre: op is at `pc` (pointing at the opcode byte). Sequence holds the bytes.
    public static bool TryGetJumpTarget(OpCode op, ReadOnlySpan<byte> sequence, int pc, out int targetPc)
    {
        switch (op)
        {
            case OpCode.Jmp:
            case OpCode.JmpUw:
            {
                // S layout: [op][hi][lo]. Target is signed 16-bit relative to the byte after the op.
                var hi = sequence[pc + 1];
                var lo = sequence[pc + 2];
                var rel = unchecked((short)((hi << 8) | lo));
                targetPc = pc + Width(op) + rel;
                return true;
            }
            case OpCode.JmpIf:
            case OpCode.JmpNot:
            case OpCode.JmpNil:
            {
                // BS layout: [op][A][hi][lo]. Same relative encoding.
                var hi = sequence[pc + 2];
                var lo = sequence[pc + 3];
                var rel = unchecked((short)((hi << 8) | lo));
                targetPc = pc + Width(op) + rel;
                return true;
            }
            default:
                targetPc = -1;
                return false;
        }
    }

    static byte[] BuildWidths()
    {
        var src = BuildLayouts();
        var dst = new byte[src.Length];
        for (var i = 0; i < src.Length; i++)
        {
            dst[i] = src[i] switch
            {
                OperandLayout.Z => 1,
                OperandLayout.B => 2,
                OperandLayout.S => 3,
                OperandLayout.BB => 3,
                OperandLayout.BS => 4,
                OperandLayout.BBB => 4,
                OperandLayout.BSS => 6,
                OperandLayout.W => 4,
                _ => 1,
            };
        }
        return dst;
    }

    static OperandLayout[] BuildLayouts()
    {
        // Default everything to Z; only assigned opcodes get their layout.
        var t = new OperandLayout[256];
        // Index reflects (byte)OpCode value. Order matches OpCode.cs.
        t[(int)OpCode.Nop] = OperandLayout.Z;
        t[(int)OpCode.Move] = OperandLayout.BB;
        t[(int)OpCode.LoadL] = OperandLayout.BB;
        t[(int)OpCode.LoadI8] = OperandLayout.BB;
        t[(int)OpCode.LoadINeg] = OperandLayout.BB;
        t[(int)OpCode.LoadI__1] = OperandLayout.B;
        t[(int)OpCode.LoadI_0] = OperandLayout.B;
        t[(int)OpCode.LoadI_1] = OperandLayout.B;
        t[(int)OpCode.LoadI_2] = OperandLayout.B;
        t[(int)OpCode.LoadI_3] = OperandLayout.B;
        t[(int)OpCode.LoadI_4] = OperandLayout.B;
        t[(int)OpCode.LoadI_5] = OperandLayout.B;
        t[(int)OpCode.LoadI_6] = OperandLayout.B;
        t[(int)OpCode.LoadI_7] = OperandLayout.B;
        t[(int)OpCode.LoadI16] = OperandLayout.BS;
        t[(int)OpCode.LoadI32] = OperandLayout.BSS;
        t[(int)OpCode.LoadSym] = OperandLayout.BB;
        t[(int)OpCode.LoadNil] = OperandLayout.B;
        t[(int)OpCode.LoadSelf] = OperandLayout.B;
        t[(int)OpCode.LoadT] = OperandLayout.B;
        t[(int)OpCode.LoadF] = OperandLayout.B;
        t[(int)OpCode.GetGV] = OperandLayout.BB;
        t[(int)OpCode.SetGV] = OperandLayout.BB;
        t[(int)OpCode.GetSV] = OperandLayout.BB;
        t[(int)OpCode.SetSV] = OperandLayout.BB;
        t[(int)OpCode.GetIV] = OperandLayout.BB;
        t[(int)OpCode.SetIV] = OperandLayout.BB;
        t[(int)OpCode.GetCV] = OperandLayout.BB;
        t[(int)OpCode.SetCV] = OperandLayout.BB;
        t[(int)OpCode.GetConst] = OperandLayout.BB;
        t[(int)OpCode.SetConst] = OperandLayout.BB;
        t[(int)OpCode.GetMCnst] = OperandLayout.BB;
        t[(int)OpCode.SetMCnst] = OperandLayout.BB;
        t[(int)OpCode.GetUpVar] = OperandLayout.BBB;
        t[(int)OpCode.SetUpVar] = OperandLayout.BBB;
        t[(int)OpCode.GetIdx] = OperandLayout.B;
        t[(int)OpCode.GetIdx0] = OperandLayout.BB;
        t[(int)OpCode.SetIdx] = OperandLayout.B;
        t[(int)OpCode.Jmp] = OperandLayout.S;
        t[(int)OpCode.JmpIf] = OperandLayout.BS;
        t[(int)OpCode.JmpNot] = OperandLayout.BS;
        t[(int)OpCode.JmpNil] = OperandLayout.BS;
        t[(int)OpCode.JmpUw] = OperandLayout.S;
        t[(int)OpCode.Except] = OperandLayout.B;
        t[(int)OpCode.Rescue] = OperandLayout.BB;
        t[(int)OpCode.RaiseIf] = OperandLayout.B;
        t[(int)OpCode.MatchErr] = OperandLayout.B;
        t[(int)OpCode.SSend] = OperandLayout.BBB;
        t[(int)OpCode.SSend0] = OperandLayout.BB;
        t[(int)OpCode.SSendB] = OperandLayout.BBB;
        t[(int)OpCode.Send] = OperandLayout.BBB;
        t[(int)OpCode.Send0] = OperandLayout.BB;
        t[(int)OpCode.SendB] = OperandLayout.BBB;
        t[(int)OpCode.Call] = OperandLayout.Z;
        t[(int)OpCode.BlkCall] = OperandLayout.BB;
        t[(int)OpCode.Super] = OperandLayout.BB;
        t[(int)OpCode.ArgAry] = OperandLayout.BS;
        t[(int)OpCode.Enter] = OperandLayout.W;
        t[(int)OpCode.KeyP] = OperandLayout.BB;
        t[(int)OpCode.KeyEnd] = OperandLayout.Z;
        t[(int)OpCode.KArg] = OperandLayout.BB;
        t[(int)OpCode.Return] = OperandLayout.B;
        t[(int)OpCode.ReturnBlk] = OperandLayout.B;
        t[(int)OpCode.RetSelf] = OperandLayout.Z;
        t[(int)OpCode.RetNil] = OperandLayout.Z;
        t[(int)OpCode.RetTrue] = OperandLayout.Z;
        t[(int)OpCode.RetFalse] = OperandLayout.Z;
        t[(int)OpCode.Break] = OperandLayout.B;
        t[(int)OpCode.BlkPush] = OperandLayout.BS;
        t[(int)OpCode.Add] = OperandLayout.B;
        t[(int)OpCode.AddI] = OperandLayout.BB;
        t[(int)OpCode.Sub] = OperandLayout.B;
        t[(int)OpCode.SubI] = OperandLayout.BB;
        t[(int)OpCode.AddILV] = OperandLayout.BBB;
        t[(int)OpCode.SubILV] = OperandLayout.BBB;
        t[(int)OpCode.Mul] = OperandLayout.B;
        t[(int)OpCode.Div] = OperandLayout.B;
        t[(int)OpCode.EQ] = OperandLayout.B;
        t[(int)OpCode.LT] = OperandLayout.B;
        t[(int)OpCode.LE] = OperandLayout.B;
        t[(int)OpCode.GT] = OperandLayout.B;
        t[(int)OpCode.GE] = OperandLayout.B;
        t[(int)OpCode.Array] = OperandLayout.BB;
        t[(int)OpCode.Array2] = OperandLayout.BBB;
        t[(int)OpCode.AryCat] = OperandLayout.B;
        t[(int)OpCode.AryPush] = OperandLayout.BB;
        t[(int)OpCode.ArySplat] = OperandLayout.B;
        t[(int)OpCode.ARef] = OperandLayout.BBB;
        t[(int)OpCode.ASet] = OperandLayout.BBB;
        t[(int)OpCode.APost] = OperandLayout.BBB;
        t[(int)OpCode.Intern] = OperandLayout.B;
        t[(int)OpCode.Symbol] = OperandLayout.BB;
        t[(int)OpCode.String] = OperandLayout.BB;
        t[(int)OpCode.StrCat] = OperandLayout.B;
        t[(int)OpCode.Hash] = OperandLayout.BB;
        t[(int)OpCode.HashAdd] = OperandLayout.BB;
        t[(int)OpCode.HashCat] = OperandLayout.B;
        t[(int)OpCode.Lambda] = OperandLayout.BB;
        t[(int)OpCode.Block] = OperandLayout.BB;
        t[(int)OpCode.Method] = OperandLayout.BB;
        t[(int)OpCode.RangeInc] = OperandLayout.B;
        t[(int)OpCode.RangeExc] = OperandLayout.B;
        t[(int)OpCode.OClass] = OperandLayout.B;
        t[(int)OpCode.Class] = OperandLayout.BB;
        t[(int)OpCode.Module] = OperandLayout.BB;
        t[(int)OpCode.Exec] = OperandLayout.BB;
        t[(int)OpCode.Def] = OperandLayout.BB;
        // TDef/SDef are BBB in the actual VM (see MRubyState.Vm.cs: OperandBBB.Read).
        // Earlier table entries were B / BB, which made HirBuilder misalign the
        // bytecode walk on top-level scripts that use `def name`.
        t[(int)OpCode.TDef] = OperandLayout.BBB;
        t[(int)OpCode.SDef] = OperandLayout.BBB;
        t[(int)OpCode.Alias] = OperandLayout.BB;
        t[(int)OpCode.Undef] = OperandLayout.B;
        t[(int)OpCode.SClass] = OperandLayout.B;
        t[(int)OpCode.TClass] = OperandLayout.B;
        t[(int)OpCode.Debug] = OperandLayout.BBB;
        t[(int)OpCode.Err] = OperandLayout.B;
        t[(int)OpCode.EXT1] = OperandLayout.Z;
        t[(int)OpCode.EXT2] = OperandLayout.Z;
        t[(int)OpCode.EXT3] = OperandLayout.Z;
        t[(int)OpCode.Stop] = OperandLayout.Z;
        return t;
    }
}
