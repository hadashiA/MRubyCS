using System.Collections.Generic;

namespace MRubyCS.Hir.Lowering;

// Resolve phi-merges into a sequence of register Move operations.
//
// Background: in HIR, edge.Args[i] flows into target.Params[i] when control
// transfers along the edge. After register allocation, each side has a
// physical register, so the transfer is "for each i, copy edge.Args[i].reg
// into target.Params[i].reg". These copies execute in parallel semantically
// (all reads happen before any write); naively serializing them can corrupt
// values when the source/destination sets overlap or form cycles.
//
// Algorithm (Belady, the classic):
//   1. Discard self-copies and copies where the source and dest are the same.
//   2. Repeatedly emit any copy whose destination is not the source of any
//      pending copy. Such a copy is "ready": its destination's old value is
//      no longer needed by anyone.
//   3. If pending copies remain, they form a cycle. Save one source register
//      to a scratch register, rewrite copies that read from that source to
//      read from the scratch instead, then return to step 2.
internal static class PhiResolver
{
    /// <summary>
    /// Emit Move ops to realize the parallel copy `srcRegs[i] -> dstRegs[i]`
    /// for each i. <paramref name="scratchReg"/> is used to break cycles and
    /// must not appear in <paramref name="srcRegs"/> or <paramref name="dstRegs"/>.
    /// </summary>
    public static void Emit(BytecodeBuilder bb, IReadOnlyList<int> srcRegs, IReadOnlyList<int> dstRegs, int scratchReg)
    {
        if (srcRegs.Count != dstRegs.Count)
        {
            throw new System.ArgumentException("srcRegs and dstRegs must have equal length");
        }

        var pending = new List<(int Src, int Dst)>(srcRegs.Count);
        for (var i = 0; i < srcRegs.Count; i++)
        {
            if (srcRegs[i] == dstRegs[i]) continue;       // identity copy
            if (srcRegs[i] < 0 || dstRegs[i] < 0) continue; // detached / dead
            pending.Add((srcRegs[i], dstRegs[i]));
        }

        while (pending.Count > 0)
        {
            // Find a "ready" copy: its dst doesn't appear as anyone's src.
            var progressed = false;
            for (var i = 0; i < pending.Count; i++)
            {
                var c = pending[i];
                if (IsReady(pending, c.Dst))
                {
                    bb.EmitBB(OpCode.Move, (byte)c.Dst, (byte)c.Src);
                    pending.RemoveAt(i);
                    progressed = true;
                    break;
                }
            }
            if (progressed) continue;

            // No ready copy => everything pending is in a cycle. Break by
            // saving one cycle's source into the scratch register, then
            // rewriting all readers of that source to read from scratch.
            var pivot = pending[0];
            if (pivot.Src == scratchReg || pivot.Dst == scratchReg)
            {
                throw new System.InvalidOperationException(
                    "scratchReg overlaps with phi register set");
            }
            bb.EmitBB(OpCode.Move, (byte)scratchReg, (byte)pivot.Src);
            for (var i = 0; i < pending.Count; i++)
            {
                if (pending[i].Src == pivot.Src)
                {
                    pending[i] = (scratchReg, pending[i].Dst);
                }
            }
        }
    }

    static bool IsReady(List<(int Src, int Dst)> pending, int dst)
    {
        foreach (var c in pending)
        {
            if (c.Src == dst) return false;
        }
        return true;
    }
}
