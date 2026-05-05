using System;
using System.Collections.Generic;
using System.Linq;
using MRubyCS.Internals;

namespace MRubyCS.Hir;

// Bytecode -> HIR. Strategy (based on ZJIT):
//
//   Phase 1: walk the bytecode to identify basic block leaders.
//   Phase 2: allocate one HirBlock per leader.
//   Phase 3: each block gets RegisterCount Params (one per register). Most are
//            dead and will be cleaned up by a later DCE pass; encoding them
//            uniformly keeps the lifter simple.
//   Phase 4: per block, lift its bytecode into HirInsns. The lifter maintains a
//            FrameState (InsnId[]) mapping each register to its current SSA
//            value within the block; reads pull from the FrameState; writes
//            replace it. Block reads with no prior local write fall back to
//            the block's parameter for that register.
//   Phase 5: connect edges based on each block's terminator. Each edge carries
//            the FrameState as args, in matching order with the target's params.
//
// SSA construction is therefore implicit: target Params receive the Union of
// argument types via type inference (ZJIT-style fixed point), and per-block
// reads can never escape the local FrameState.
static class HirBuilder
{
    public static HirFunction Build(Irep irep)
    {
        var func = new HirFunction(irep);
        var seq = irep.Sequence;
        if (seq.Length == 0)
        {
            var b = func.NewBlock(0, 0);
            func.EntryBlock = b.Id;
            return func;
        }

        var leaders = CollectLeaders(seq);
        var pcToBlock = AllocateBlocks(func, leaders, seq.Length);
        AllocateParams(func);
        EmitBlocks(func, irep, pcToBlock);
        return func;
    }

    static SortedSet<int> CollectLeaders(ReadOnlySpan<byte> seq)
    {
        var leaders = new SortedSet<int> { 0 };
        for (var pc = 0; pc < seq.Length;)
        {
            var op = (OpCode)seq[pc];
            var width = OpCodeInfo.Width(op);
            if (pc + width > seq.Length) break;

            if (OpCodeInfo.TryGetJumpTarget(op, seq, pc, out var target))
            {
                if (target >= 0 && target < seq.Length) leaders.Add(target);
            }
            if (OpCodeInfo.IsTerminator(op) || OpCodeInfo.IsBranch(op))
            {
                var fall = pc + width;
                if (fall < seq.Length) leaders.Add(fall);
            }
            pc += width;
        }
        return leaders;
    }

    static Dictionary<int, BlockId> AllocateBlocks(HirFunction func, SortedSet<int> leaders, int seqLen)
    {
        var arr = leaders.ToArray();
        var map = new Dictionary<int, BlockId>(arr.Length);
        for (var i = 0; i < arr.Length; i++)
        {
            var start = arr[i];
            var end = i + 1 < arr.Length ? arr[i + 1] : seqLen;
            var b = func.NewBlock(start, end);
            map[start] = b.Id;
        }
        if (arr.Length > 0) func.EntryBlock = map[0];
        return map;
    }

    // Allocate RegisterCount params per block. Register r maps to Block.Params[r].
    static void AllocateParams(HirFunction func)
    {
        foreach (var block in func.Blocks)
        {
            for (var r = 0; r < func.RegisterCount; r++)
            {
                func.PushParam(block.Id, r);
            }
        }
    }

    static void EmitBlocks(HirFunction func, Irep irep, Dictionary<int, BlockId> pcToBlock)
    {
        var seq = irep.Sequence;
        foreach (var block in func.Blocks)
        {
            // FrameState: register -> current SSA value within this block.
            var frame = new InsnId[func.RegisterCount];
            for (var r = 0; r < func.RegisterCount; r++) frame[r] = block.Params[r];

            for (var pc = block.StartPc; pc < block.EndPc;)
            {
                var op = (OpCode)seq[pc];
                var width = OpCodeInfo.Width(op);
                if (pc + width > seq.Length) break;
                Lift(func, block, irep, op, pc, frame);
                pc += width;
            }

            ConnectTerminator(func, block, frame, pcToBlock);
        }
    }

    static void ConnectTerminator(HirFunction func, HirBlock block, InsnId[] frame, Dictionary<int, BlockId> pcToBlock)
    {
        if (block.Insns.Count == 0)
        {
            // Empty block (rare); fall through to next sequential block if any.
            if (pcToBlock.TryGetValue(block.EndPc, out var next))
            {
                var edge = func.NewEdge(block.Id, next);
                AppendArgs(edge, frame);
            }
            return;
        }
        var lastId = block.Insns[^1];
        var last = func[lastId];
        switch (last.Kind)
        {
            case HirInsnKind.Jump:
                if (pcToBlock.TryGetValue(last.Aux1, out var jt))
                {
                    var edge = func.NewEdge(block.Id, jt);
                    AppendArgs(edge, frame);
                }
                break;
            case HirInsnKind.BranchIf:
            case HirInsnKind.BranchUnless:
            case HirInsnKind.BranchNil:
                // Successor 0 = taken, 1 = fallthrough.
                if (pcToBlock.TryGetValue(last.Aux1, out var taken))
                {
                    var e = func.NewEdge(block.Id, taken);
                    AppendArgs(e, frame);
                }
                if (pcToBlock.TryGetValue(block.EndPc, out var fall))
                {
                    var e = func.NewEdge(block.Id, fall);
                    AppendArgs(e, frame);
                }
                break;
            case HirInsnKind.Return:
            case HirInsnKind.ReturnBlk:
            case HirInsnKind.Break:
            case HirInsnKind.Stop:
                // function exit; no successor
                break;
            default:
                // No terminator? Fall through to next sequential block.
                if (pcToBlock.TryGetValue(block.EndPc, out var next))
                {
                    var edge = func.NewEdge(block.Id, next);
                    AppendArgs(edge, frame);
                }
                break;
        }
    }

    static void AppendArgs(HirBranchEdge edge, InsnId[] frame)
    {
        edge.Args.Capacity = frame.Length;
        foreach (var v in frame) edge.Args.Add(v);
    }

    // -----------------------------------------------------------------
    // Per-opcode lifters
    // -----------------------------------------------------------------

    static InsnId Read(InsnId[] frame, int reg) =>
        (uint)reg < (uint)frame.Length ? frame[reg] : InsnId.Invalid;

    static InsnId Write(HirFunction func, BlockId block, InsnId[] frame, int reg, HirInsn insn)
    {
        var id = func.Push(block, insn);
        if ((uint)reg < (uint)frame.Length) frame[reg] = id;
        return id;
    }

    static void Lift(HirFunction func, HirBlock block, Irep irep, OpCode op, int pc, InsnId[] frame)
    {
        var seq = irep.Sequence;
        var symbols = irep.Symbols;
        var pool = irep.PoolValues;

        switch (op)
        {
            case OpCode.Nop:
                func.Push(block.Id, new HirInsn(HirInsnKind.Nop, pc, op));
                return;

            case OpCode.Move:
            {
                var a = seq[pc + 1]; var b = seq[pc + 2];
                var src = Read(frame, b);
                var insn = new HirInsn(HirInsnKind.Move, pc, op);
                insn.Inputs.Add(src);
                Write(func, block.Id, frame, a, insn);
                return;
            }

            case OpCode.LoadL:
            {
                var a = seq[pc + 1]; var b = seq[pc + 2];
                var insn = new HirInsn(HirInsnKind.LoadPool, pc, op)
                {
                    Aux1 = b,
                    AuxObj = b < pool.Length ? (object)pool[b] : null,
                };
                Write(func, block.Id, frame, a, insn);
                return;
            }

            case OpCode.LoadI8:
            case OpCode.LoadINeg:
            case OpCode.LoadI__1:
            case OpCode.LoadI_0:
            case OpCode.LoadI_1:
            case OpCode.LoadI_2:
            case OpCode.LoadI_3:
            case OpCode.LoadI_4:
            case OpCode.LoadI_5:
            case OpCode.LoadI_6:
            case OpCode.LoadI_7:
            case OpCode.LoadI16:
            case OpCode.LoadI32:
            {
                var a = seq[pc + 1];
                int v = op switch
                {
                    OpCode.LoadI8 => seq[pc + 2],
                    OpCode.LoadINeg => -seq[pc + 2],
                    OpCode.LoadI__1 => -1,
                    OpCode.LoadI_0 or OpCode.LoadI_1 or OpCode.LoadI_2 or OpCode.LoadI_3
                        or OpCode.LoadI_4 or OpCode.LoadI_5 or OpCode.LoadI_6 or OpCode.LoadI_7
                        => (int)op - (int)OpCode.LoadI_0,
                    OpCode.LoadI16 => unchecked((short)((seq[pc + 2] << 8) | seq[pc + 3])),
                    OpCode.LoadI32 => unchecked((int)(((uint)seq[pc + 2] << 24) | ((uint)seq[pc + 3] << 16) | ((uint)seq[pc + 4] << 8) | seq[pc + 5])),
                    _ => 0,
                };
                var insn = new HirInsn(HirInsnKind.LoadInt, pc, op) { Aux1 = v };
                Write(func, block.Id, frame, a, insn);
                return;
            }

            case OpCode.LoadSym:
            {
                var a = seq[pc + 1]; var b = seq[pc + 2];
                var insn = new HirInsn(HirInsnKind.LoadSym, pc, op)
                {
                    AuxSymbol = b < symbols.Length ? symbols[b] : default,
                };
                Write(func, block.Id, frame, a, insn);
                return;
            }

            case OpCode.LoadNil:
            case OpCode.LoadSelf:
            case OpCode.LoadT:
            case OpCode.LoadF:
            {
                var a = seq[pc + 1];
                var kind = op switch
                {
                    OpCode.LoadNil => HirInsnKind.LoadNil,
                    OpCode.LoadSelf => HirInsnKind.LoadSelf,
                    OpCode.LoadT => HirInsnKind.LoadTrue,
                    _ => HirInsnKind.LoadFalse,
                };
                Write(func, block.Id, frame, a, new HirInsn(kind, pc, op));
                return;
            }

            case OpCode.GetIV: BBLoad(func, block, frame, pc, op, seq, symbols, HirInsnKind.GetIV); return;
            case OpCode.SetIV: BBStore(func, block, frame, pc, op, seq, symbols, HirInsnKind.SetIV); return;
            case OpCode.GetGV: BBLoad(func, block, frame, pc, op, seq, symbols, HirInsnKind.GetGV); return;
            case OpCode.SetGV: BBStore(func, block, frame, pc, op, seq, symbols, HirInsnKind.SetGV); return;
            case OpCode.GetCV: BBLoad(func, block, frame, pc, op, seq, symbols, HirInsnKind.GetCV); return;
            case OpCode.SetCV: BBStore(func, block, frame, pc, op, seq, symbols, HirInsnKind.SetCV); return;
            case OpCode.GetConst: BBLoad(func, block, frame, pc, op, seq, symbols, HirInsnKind.GetConst); return;
            case OpCode.SetConst: BBStore(func, block, frame, pc, op, seq, symbols, HirInsnKind.SetConst); return;

            case OpCode.GetUpVar:
            {
                var a = seq[pc + 1]; var b = seq[pc + 2]; var c = seq[pc + 3];
                var insn = new HirInsn(HirInsnKind.GetUpVar, pc, op) { Aux1 = b, Aux2 = c };
                Write(func, block.Id, frame, a, insn);
                return;
            }
            case OpCode.SetUpVar:
            {
                var a = seq[pc + 1]; var b = seq[pc + 2]; var c = seq[pc + 3];
                var src = Read(frame, a);
                var insn = new HirInsn(HirInsnKind.SetUpVar, pc, op) { Aux1 = b, Aux2 = c };
                insn.Inputs.Add(src);
                func.Push(block.Id, insn);
                return;
            }

            case OpCode.Add: case OpCode.Sub: case OpCode.Mul: case OpCode.Div:
            case OpCode.EQ: case OpCode.LT: case OpCode.LE: case OpCode.GT: case OpCode.GE:
            {
                var a = seq[pc + 1];
                var lhs = Read(frame, a);
                var rhs = Read(frame, a + 1);
                var kind = op switch
                {
                    OpCode.Add => HirInsnKind.Add,
                    OpCode.Sub => HirInsnKind.Sub,
                    OpCode.Mul => HirInsnKind.Mul,
                    OpCode.Div => HirInsnKind.Div,
                    OpCode.EQ => HirInsnKind.Eq,
                    OpCode.LT => HirInsnKind.Lt,
                    OpCode.LE => HirInsnKind.Le,
                    OpCode.GT => HirInsnKind.Gt,
                    _ => HirInsnKind.Ge,
                };
                var insn = new HirInsn(kind, pc, op);
                insn.Inputs.Add(lhs);
                insn.Inputs.Add(rhs);
                Write(func, block.Id, frame, a, insn);
                return;
            }

            case OpCode.AddI:
            case OpCode.SubI:
            {
                var a = seq[pc + 1]; var b = seq[pc + 2];
                var lhs = Read(frame, a);
                var insn = new HirInsn(op == OpCode.AddI ? HirInsnKind.AddI : HirInsnKind.SubI, pc, op) { Aux1 = b };
                insn.Inputs.Add(lhs);
                Write(func, block.Id, frame, a, insn);
                return;
            }

            case OpCode.Array:
            {
                var a = seq[pc + 1]; var n = seq[pc + 2];
                var insn = new HirInsn(HirInsnKind.NewArray, pc, op) { Aux1 = n };
                for (var i = 0; i < n; i++) insn.Inputs.Add(Read(frame, a + i));
                Write(func, block.Id, frame, a, insn);
                return;
            }
            case OpCode.Array2:
            {
                var a = seq[pc + 1]; var b = seq[pc + 2]; var n = seq[pc + 3];
                var insn = new HirInsn(HirInsnKind.NewArray, pc, op) { Aux1 = n };
                for (var i = 0; i < n; i++) insn.Inputs.Add(Read(frame, b + i));
                Write(func, block.Id, frame, a, insn);
                return;
            }
            case OpCode.Hash:
            {
                var a = seq[pc + 1]; var n = seq[pc + 2];
                var insn = new HirInsn(HirInsnKind.NewHash, pc, op) { Aux1 = n };
                for (var i = 0; i < n * 2; i++) insn.Inputs.Add(Read(frame, a + i));
                Write(func, block.Id, frame, a, insn);
                return;
            }
            case OpCode.String:
            {
                var a = seq[pc + 1]; var b = seq[pc + 2];
                var insn = new HirInsn(HirInsnKind.NewString, pc, op)
                {
                    Aux1 = b,
                    AuxObj = b < pool.Length ? (object)pool[b] : null,
                };
                Write(func, block.Id, frame, a, insn);
                return;
            }
            case OpCode.Lambda:
            case OpCode.Block:
            case OpCode.Method:
            {
                var a = seq[pc + 1]; var b = seq[pc + 2];
                var kind = op switch
                {
                    OpCode.Lambda => HirInsnKind.Lambda,
                    OpCode.Block => HirInsnKind.Block,
                    _ => HirInsnKind.Method,
                };
                var insn = new HirInsn(kind, pc, op) { Aux1 = b };
                Write(func, block.Id, frame, a, insn);
                return;
            }

            case OpCode.Send:
            case OpCode.Send0:
            case OpCode.SendB:
            case OpCode.SSend:
            case OpCode.SSend0:
            case OpCode.SSendB:
            {
                int a, sym, c;
                if (op is OpCode.Send0 or OpCode.SSend0)
                {
                    a = seq[pc + 1]; sym = seq[pc + 2]; c = 0;
                }
                else
                {
                    a = seq[pc + 1]; sym = seq[pc + 2]; c = seq[pc + 3];
                }
                var argc = c & 0xf;
                var kargc = (c >> 4) & 0xf;
                var hasBlock = op is OpCode.SendB or OpCode.SSendB;
                var isSelfSend = op is OpCode.SSend or OpCode.SSend0 or OpCode.SSendB;
                var insn = new HirInsn(HirInsnKind.Send, pc, op)
                {
                    AuxSymbol = sym < symbols.Length ? symbols[sym] : default,
                    Aux1 = argc,
                    Aux2 = kargc,
                    AuxBool = hasBlock,
                    AuxBool2 = isSelfSend,
                };
                if (!isSelfSend) insn.Inputs.Add(Read(frame, a));
                for (var i = 1; i <= argc; i++) insn.Inputs.Add(Read(frame, a + i));
                for (var i = 0; i < kargc * 2; i++) insn.Inputs.Add(Read(frame, a + argc + 1 + i));
                if (hasBlock) insn.Inputs.Add(Read(frame, a + argc + kargc * 2 + 1));
                Write(func, block.Id, frame, a, insn);
                return;
            }

            case OpCode.Jmp:
            {
                OpCodeInfo.TryGetJumpTarget(op, seq, pc, out var target);
                func.Push(block.Id, new HirInsn(HirInsnKind.Jump, pc, op) { Aux1 = target });
                return;
            }
            case OpCode.JmpIf:
            case OpCode.JmpNot:
            case OpCode.JmpNil:
            {
                var a = seq[pc + 1];
                OpCodeInfo.TryGetJumpTarget(op, seq, pc, out var target);
                var kind = op switch
                {
                    OpCode.JmpIf => HirInsnKind.BranchIf,
                    OpCode.JmpNot => HirInsnKind.BranchUnless,
                    _ => HirInsnKind.BranchNil,
                };
                var insn = new HirInsn(kind, pc, op) { Aux1 = target };
                insn.Inputs.Add(Read(frame, a));
                func.Push(block.Id, insn);
                return;
            }
            case OpCode.JmpUw:
            {
                OpCodeInfo.TryGetJumpTarget(op, seq, pc, out var target);
                func.Push(block.Id, new HirInsn(HirInsnKind.Jump, pc, op) { Aux1 = target });
                return;
            }

            case OpCode.Return:
            case OpCode.ReturnBlk:
            {
                var a = seq[pc + 1];
                var insn = new HirInsn(op == OpCode.Return ? HirInsnKind.Return : HirInsnKind.ReturnBlk, pc, op);
                insn.Inputs.Add(Read(frame, a));
                func.Push(block.Id, insn);
                return;
            }
            case OpCode.RetSelf:
                func.Push(block.Id, new HirInsn(HirInsnKind.Return, pc, op) { AuxBool = true });
                return;
            case OpCode.RetNil:
                func.Push(block.Id, new HirInsn(HirInsnKind.Return, pc, op) { AuxObj = "nil" });
                return;
            case OpCode.RetTrue:
                func.Push(block.Id, new HirInsn(HirInsnKind.Return, pc, op) { AuxObj = "true" });
                return;
            case OpCode.RetFalse:
                func.Push(block.Id, new HirInsn(HirInsnKind.Return, pc, op) { AuxObj = "false" });
                return;
            case OpCode.Stop:
                func.Push(block.Id, new HirInsn(HirInsnKind.Stop, pc, op));
                return;
            case OpCode.Break:
            {
                var a = seq[pc + 1];
                var insn = new HirInsn(HirInsnKind.Break, pc, op);
                insn.Inputs.Add(Read(frame, a));
                func.Push(block.Id, insn);
                return;
            }
            case OpCode.Enter:
                func.Push(block.Id, new HirInsn(HirInsnKind.Enter, pc, op));
                return;

            default:
                func.Push(block.Id, new HirInsn(HirInsnKind.Other, pc, op));
                return;
        }
    }

    static void BBLoad(HirFunction func, HirBlock block, InsnId[] frame, int pc, OpCode op, byte[] seq, Symbol[] symbols, HirInsnKind kind)
    {
        var a = seq[pc + 1]; var b = seq[pc + 2];
        var insn = new HirInsn(kind, pc, op) { AuxSymbol = b < symbols.Length ? symbols[b] : default };
        Write(func, block.Id, frame, a, insn);
    }

    static void BBStore(HirFunction func, HirBlock block, InsnId[] frame, int pc, OpCode op, byte[] seq, Symbol[] symbols, HirInsnKind kind)
    {
        var a = seq[pc + 1]; var b = seq[pc + 2];
        var src = Read(frame, a);
        var insn = new HirInsn(kind, pc, op) { AuxSymbol = b < symbols.Length ? symbols[b] : default };
        insn.Inputs.Add(src);
        func.Push(block.Id, insn);
    }
}
