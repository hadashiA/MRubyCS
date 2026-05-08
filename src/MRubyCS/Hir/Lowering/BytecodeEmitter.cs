using System.Collections.Generic;

namespace MRubyCS.Hir.Lowering;

// Walks a HirFunction in block-layout order and produces an mrb bytecode
// sequence using the given register allocation.
//
// Coverage in H1 is intentionally narrow — only the insn kinds the round-trip
// of a trivial Ruby program needs. Anything else throws so we discover gaps
// loudly during incremental development. H2 / H3 progressively widen the
// supported set.
internal sealed class BytecodeEmitter
{
    readonly HirFunction func;
    readonly BlockLayout layout;
    readonly RegisterAllocation alloc;
    readonly BytecodeBuilder bb = new();

    public BytecodeEmitter(HirFunction func, BlockLayout layout, RegisterAllocation alloc)
    {
        this.func = func;
        this.layout = layout;
        this.alloc = alloc;
    }

    public byte[] Emit()
    {
        // H1 constraint: single-block, no outgoing edges. CFG handling lands in H2.
        if (layout.Order.Count != 1)
        {
            throw new System.NotSupportedException(
                $"H1: only single-block functions are lowered; got {layout.Order.Count} blocks");
        }
        var blockId = layout.Order[0];
        var block = func[blockId];
        if (block.OutEdges.Count > 0)
        {
            throw new System.NotSupportedException(
                "H1: outgoing edges (CFG) not supported yet");
        }

        foreach (var insnId in block.Insns)
        {
            var insn = func[insnId];
            EmitInsn(insnId, insn);
        }

        return bb.ToBytes();
    }

    void EmitInsn(InsnId id, HirInsn insn)
    {
        switch (insn.Kind)
        {
            case HirInsnKind.Nop:
                bb.EmitOp(OpCode.Nop); return;

            case HirInsnKind.LoadNil:
                bb.EmitB(OpCode.LoadNil, Reg(id)); return;
            case HirInsnKind.LoadTrue:
                bb.EmitB(OpCode.LoadT, Reg(id)); return;
            case HirInsnKind.LoadFalse:
                bb.EmitB(OpCode.LoadF, Reg(id)); return;
            case HirInsnKind.LoadSelf:
                bb.EmitB(OpCode.LoadSelf, Reg(id)); return;

            case HirInsnKind.LoadInt:
                EmitLoadInt(Reg(id), insn.Aux1); return;

            case HirInsnKind.Move:
                bb.EmitBB(OpCode.Move, Reg(id), Reg(insn.Inputs[0])); return;

            case HirInsnKind.Return:
                EmitReturn(insn); return;

            case HirInsnKind.Stop:
                bb.EmitOp(OpCode.Stop); return;

            default:
                throw new System.NotSupportedException(
                    $"H1: insn kind {insn.Kind} not yet lowered (slot {id})");
        }
    }

    void EmitReturn(HirInsn insn)
    {
        // The lifter encodes the source variant of Return via Aux fields:
        //   AuxBool=true  -> RetSelf
        //   AuxObj="nil"  -> RetNil
        //   AuxObj="true" -> RetTrue
        //   AuxObj="false"-> RetFalse
        //   else          -> Return Inputs[0]
        if (insn.AuxBool)
        {
            bb.EmitOp(OpCode.RetSelf); return;
        }
        if (insn.AuxObj is string s)
        {
            bb.EmitOp(s switch
            {
                "nil" => OpCode.RetNil,
                "true" => OpCode.RetTrue,
                "false" => OpCode.RetFalse,
                _ => OpCode.RetNil,
            });
            return;
        }
        if (insn.Inputs.Count != 1)
        {
            throw new System.InvalidOperationException("Return expects exactly one input");
        }
        bb.EmitB(OpCode.Return, Reg(insn.Inputs[0]));
    }

    void EmitLoadInt(byte dst, int value)
    {
        switch (value)
        {
            case -1: bb.EmitB(OpCode.LoadI__1, dst); return;
            case 0: bb.EmitB(OpCode.LoadI_0, dst); return;
            case 1: bb.EmitB(OpCode.LoadI_1, dst); return;
            case 2: bb.EmitB(OpCode.LoadI_2, dst); return;
            case 3: bb.EmitB(OpCode.LoadI_3, dst); return;
            case 4: bb.EmitB(OpCode.LoadI_4, dst); return;
            case 5: bb.EmitB(OpCode.LoadI_5, dst); return;
            case 6: bb.EmitB(OpCode.LoadI_6, dst); return;
            case 7: bb.EmitB(OpCode.LoadI_7, dst); return;
        }
        if (value >= 0 && value <= byte.MaxValue)
        {
            bb.EmitBB(OpCode.LoadI8, dst, (byte)value); return;
        }
        if (value < 0 && value >= -byte.MaxValue)
        {
            bb.EmitBB(OpCode.LoadINeg, dst, (byte)(-value)); return;
        }
        if (value >= short.MinValue && value <= short.MaxValue)
        {
            bb.EmitBS(OpCode.LoadI16, dst, (short)value); return;
        }
        bb.EmitBSS(OpCode.LoadI32, dst, value);
    }

    byte Reg(InsnId id)
    {
        if (!alloc.TryGet(id, out var r))
        {
            throw new System.InvalidOperationException($"No register for {id} during emission");
        }
        if (r > byte.MaxValue)
        {
            throw new System.NotSupportedException($"Register {r} exceeds byte operand range");
        }
        return (byte)r;
    }
}

internal sealed class BytecodeBuilder
{
    readonly List<byte> bytes = new();

    public int Length => bytes.Count;

    public void EmitOp(OpCode op) { bytes.Add((byte)op); }
    public void EmitB(OpCode op, byte a) { bytes.Add((byte)op); bytes.Add(a); }
    public void EmitBB(OpCode op, byte a, byte b) { bytes.Add((byte)op); bytes.Add(a); bytes.Add(b); }
    public void EmitBBB(OpCode op, byte a, byte b, byte c) { bytes.Add((byte)op); bytes.Add(a); bytes.Add(b); bytes.Add(c); }
    public void EmitS(OpCode op, short s)
    {
        bytes.Add((byte)op);
        bytes.Add((byte)((s >> 8) & 0xff));
        bytes.Add((byte)(s & 0xff));
    }
    public void EmitBS(OpCode op, byte a, short s)
    {
        bytes.Add((byte)op);
        bytes.Add(a);
        bytes.Add((byte)((s >> 8) & 0xff));
        bytes.Add((byte)(s & 0xff));
    }
    public void EmitBSS(OpCode op, byte a, int v)
    {
        bytes.Add((byte)op);
        bytes.Add(a);
        bytes.Add((byte)((v >> 24) & 0xff));
        bytes.Add((byte)((v >> 16) & 0xff));
        bytes.Add((byte)((v >> 8) & 0xff));
        bytes.Add((byte)(v & 0xff));
    }

    public byte[] ToBytes() => bytes.ToArray();
}
