using System.Collections.Generic;

namespace MRubyCS.Hir.Lowering;

// Walks a HirFunction in block-layout order and produces an mrb bytecode
// sequence using the given register allocation.
//
// Coverage progresses by phase:
//   * H1: single-block, edge-free, simple Load/Move/Return/Stop.
//   * H2: multi-block CFG, arithmetic / comparison / conditional & uncond.
//         branches via scratch window and parallel-copy phi resolution.
//   * H3+: Send / arrays / hashes / lambdas (operand-adjacency required).
//
// Anything not yet covered throws NotSupportedException so unsupported insns
// surface loudly during incremental development.
internal sealed class BytecodeEmitter
{
    readonly HirFunction func;
    readonly BlockLayout layout;
    readonly RegisterAllocation alloc;
    readonly int scratchReg0;
    readonly int scratchReg1;
    readonly BytecodeBuilder bb = new();
    readonly Dictionary<int, int> blockStartPc = new();
    readonly List<JumpPatch> patches = new();

    readonly record struct JumpPatch(int OffsetSlotPc, BlockId Target, int InstrEndPc);

    public BytecodeEmitter(HirFunction func, BlockLayout layout, RegisterAllocation alloc,
        int scratchReg0, int scratchReg1)
    {
        this.func = func;
        this.layout = layout;
        this.alloc = alloc;
        this.scratchReg0 = scratchReg0;
        this.scratchReg1 = scratchReg1;
    }

    public byte[] Emit()
    {
        for (var i = 0; i < layout.Order.Count; i++)
        {
            var blockId = layout.Order[i];
            blockStartPc[blockId.Value] = bb.Length;
            var block = func[blockId];

            // Emit non-terminator insns first.
            HirInsn? terminator = null;
            foreach (var insnId in block.Insns)
            {
                var insn = func[insnId];
                if (insn.IsTerminator)
                {
                    if (terminator != null)
                    {
                        throw new System.InvalidOperationException(
                            "block has more than one terminator");
                    }
                    terminator = insn;
                    continue;
                }
                EmitInsn(insnId, insn);
            }

            // Then the terminator (and its outgoing edges' phi copies).
            EmitTerminator(block, terminator, i);
        }

        ApplyPatches();
        return bb.ToBytes();
    }

    void EmitTerminator(HirBlock block, HirInsn? terminator, int layoutIdx)
    {
        if (terminator == null)
        {
            // Block falls through implicitly. If there's an outgoing edge,
            // emit phi copies + (optional) Jmp.
            if (block.OutEdges.Count == 0) return;
            if (block.OutEdges.Count > 1)
            {
                throw new System.InvalidOperationException(
                    "block without explicit terminator must have <= 1 outgoing edge");
            }
            EmitPhiCopiesForEdge(block.OutEdges[0]);
            if (IsNextInLayout(block.OutEdges[0].Target, layoutIdx)) return;
            EmitJump(block.OutEdges[0].Target);
            return;
        }

        switch (terminator.Kind)
        {
            case HirInsnKind.Jump:
                EmitJumpEdge(block, layoutIdx);
                return;
            case HirInsnKind.BranchIf:
            case HirInsnKind.BranchUnless:
            case HirInsnKind.BranchNil:
                EmitConditional(block, terminator, layoutIdx);
                return;
            case HirInsnKind.Return:
                EmitReturn(terminator);
                return;
            case HirInsnKind.Stop:
                bb.EmitOp(OpCode.Stop);
                return;
            default:
                throw new System.NotSupportedException(
                    $"H2: terminator {terminator.Kind} not yet lowered");
        }
    }

    void EmitJumpEdge(HirBlock block, int layoutIdx)
    {
        if (block.OutEdges.Count != 1)
        {
            throw new System.InvalidOperationException(
                "Jump expects exactly one outgoing edge");
        }
        var edge = block.OutEdges[0];
        EmitPhiCopiesForEdge(edge);
        if (IsNextInLayout(edge.Target, layoutIdx)) return;
        EmitJump(edge.Target);
    }

    void EmitConditional(HirBlock block, HirInsn term, int layoutIdx)
    {
        if (block.OutEdges.Count != 2)
        {
            throw new System.InvalidOperationException(
                "Conditional terminator expects exactly two outgoing edges");
        }
        var taken = block.OutEdges[0];
        var fall = block.OutEdges[1];
        // For H2 we don't synthesize the trampoline blocks needed when a
        // conditional successor is itself a phi merge point. PhiSimplify
        // collapses single-predecessor blocks, so this only fires in
        // contrived CFGs we'll handle in H3 with critical-edge splitting.
        EnsureNoPhiCopies(taken);
        EnsureNoPhiCopies(fall);

        var condReg = Reg(term.Inputs[0]);
        var op = term.Kind switch
        {
            HirInsnKind.BranchIf => OpCode.JmpIf,
            HirInsnKind.BranchUnless => OpCode.JmpNot,
            HirInsnKind.BranchNil => OpCode.JmpNil,
            _ => throw new System.InvalidOperationException(),
        };
        EmitConditionalJumpToBlock(op, condReg, taken.Target);
        if (IsNextInLayout(fall.Target, layoutIdx)) return;
        EmitJump(fall.Target);
    }

    void EmitInsn(InsnId id, HirInsn insn)
    {
        switch (insn.Kind)
        {
            case HirInsnKind.Nop:
                bb.EmitOp(OpCode.Nop); return;

            case HirInsnKind.Param:
                // Params are pseudo-insns: their value lives in a register
                // by virtue of allocation, no bytecode emission needed.
                return;

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

            // Arithmetic: operands must be in consecutive registers per
            // mruby's R[a] / R[a+1] convention. We materialize them in the
            // scratch window, then move the result back into the allocated
            // destination.
            case HirInsnKind.Add: EmitBinaryArith(id, insn, OpCode.Add); return;
            case HirInsnKind.Sub: EmitBinaryArith(id, insn, OpCode.Sub); return;
            case HirInsnKind.Mul: EmitBinaryArith(id, insn, OpCode.Mul); return;
            case HirInsnKind.Div: EmitBinaryArith(id, insn, OpCode.Div); return;
            case HirInsnKind.Eq: EmitBinaryArith(id, insn, OpCode.EQ); return;
            case HirInsnKind.Lt: EmitBinaryArith(id, insn, OpCode.LT); return;
            case HirInsnKind.Le: EmitBinaryArith(id, insn, OpCode.LE); return;
            case HirInsnKind.Gt: EmitBinaryArith(id, insn, OpCode.GT); return;
            case HirInsnKind.Ge: EmitBinaryArith(id, insn, OpCode.GE); return;

            case HirInsnKind.AddI: EmitImmediateArith(id, insn, OpCode.AddI); return;
            case HirInsnKind.SubI: EmitImmediateArith(id, insn, OpCode.SubI); return;

            default:
                throw new System.NotSupportedException(
                    $"H2: insn kind {insn.Kind} not yet lowered (slot {id})");
        }
    }

    void EmitBinaryArith(InsnId id, HirInsn insn, OpCode op)
    {
        var lhsReg = Reg(insn.Inputs[0]);
        var rhsReg = Reg(insn.Inputs[1]);
        var dstReg = Reg(id);
        // R[scratch0] <- lhs ; R[scratch1] <- rhs ; <op> R[scratch0] ; dst <- R[scratch0]
        bb.EmitBB(OpCode.Move, (byte)scratchReg0, lhsReg);
        bb.EmitBB(OpCode.Move, (byte)scratchReg1, rhsReg);
        bb.EmitB(op, (byte)scratchReg0);
        if (dstReg != scratchReg0)
        {
            bb.EmitBB(OpCode.Move, dstReg, (byte)scratchReg0);
        }
    }

    void EmitImmediateArith(InsnId id, HirInsn insn, OpCode op)
    {
        var lhsReg = Reg(insn.Inputs[0]);
        var dstReg = Reg(id);
        if (insn.Aux1 < 0 || insn.Aux1 > byte.MaxValue)
        {
            throw new System.NotSupportedException(
                $"{op} immediate {insn.Aux1} out of byte range; expand to LoadInt + binary op upstream");
        }
        bb.EmitBB(OpCode.Move, (byte)scratchReg0, lhsReg);
        bb.EmitBB(op, (byte)scratchReg0, (byte)insn.Aux1);
        if (dstReg != scratchReg0)
        {
            bb.EmitBB(OpCode.Move, dstReg, (byte)scratchReg0);
        }
    }

    void EmitReturn(HirInsn insn)
    {
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

    void EmitPhiCopiesForEdge(HirBranchEdge edge)
    {
        var target = func[edge.Target];
        var srcRegs = new List<int>();
        var dstRegs = new List<int>();
        for (var i = 0; i < edge.Args.Count && i < target.Params.Count; i++)
        {
            var arg = edge.Args[i];
            var paramId = target.Params[i];
            var paramInsn = func[paramId];
            if (paramInsn.Kind != HirInsnKind.Param) continue;
            if (!arg.IsValid) continue;
            if (!alloc.TryGet(arg, out var src)) continue;
            if (!alloc.TryGet(paramId, out var dst)) continue;
            srcRegs.Add(src);
            dstRegs.Add(dst);
        }
        PhiResolver.Emit(bb, srcRegs, dstRegs, scratchReg0);
    }

    void EnsureNoPhiCopies(HirBranchEdge edge)
    {
        var target = func[edge.Target];
        for (var i = 0; i < edge.Args.Count && i < target.Params.Count; i++)
        {
            var arg = edge.Args[i];
            var paramId = target.Params[i];
            var paramInsn = func[paramId];
            if (paramInsn.Kind != HirInsnKind.Param) continue;
            if (!arg.IsValid) continue;
            if (!alloc.TryGet(arg, out var src)) continue;
            if (!alloc.TryGet(paramId, out var dst)) continue;
            if (src != dst)
            {
                throw new System.NotSupportedException(
                    "H2: phi copies on conditional successor require critical-edge splitting (H3)");
            }
        }
    }

    bool IsNextInLayout(BlockId target, int currentIdx)
    {
        return currentIdx + 1 < layout.Order.Count
            && layout.Order[currentIdx + 1] == target;
    }

    void EmitJump(BlockId target)
    {
        // OP_Jmp: S layout. 3 bytes total. Offset slot at pc+1, instr ends at pc+3.
        var pc = bb.Length;
        bb.EmitS(OpCode.Jmp, 0);
        patches.Add(new JumpPatch(pc + 1, target, pc + 3));
    }

    void EmitConditionalJumpToBlock(OpCode op, byte cond, BlockId target)
    {
        // BS layout: 4 bytes total. Offset slot at pc+2, instr ends at pc+4.
        var pc = bb.Length;
        bb.EmitBS(op, cond, 0);
        patches.Add(new JumpPatch(pc + 2, target, pc + 4));
    }

    void ApplyPatches()
    {
        foreach (var p in patches)
        {
            if (!blockStartPc.TryGetValue(p.Target.Value, out var targetPc))
            {
                throw new System.InvalidOperationException(
                    $"jump patch references unlaid block {p.Target}");
            }
            var rel = targetPc - p.InstrEndPc;
            if (rel < short.MinValue || rel > short.MaxValue)
            {
                throw new System.NotSupportedException(
                    $"jump offset {rel} out of i16 range; long-jump trampoline needed");
            }
            bb.PatchS(p.OffsetSlotPc, (short)rel);
        }
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

    /// <summary>Patch a 16-bit big-endian value at <paramref name="pc"/>.</summary>
    public void PatchS(int pc, short value)
    {
        bytes[pc] = (byte)((value >> 8) & 0xff);
        bytes[pc + 1] = (byte)(value & 0xff);
    }

    public byte[] ToBytes() => bytes.ToArray();
}
