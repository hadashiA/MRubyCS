using System.Collections.Generic;

namespace MRubyCS.Hir.Lowering;

// Standard linear-scan register allocation, no spilling (we allocate as many
// registers as needed). Spilling can be added when register pressure becomes
// observable, but mruby's stack-allocated frame model means a wider register
// file is essentially free up to the C# stack reservation budget.
//
// Reservation policy:
//   * Register 0 is reserved for `self` in every Ruby callable. We therefore
//     pre-allocate it to no SSA value and start the free pool at 1.
//   * Entry-block Params (slot k) are pinned to register `k` since the caller
//     places method args at consecutive registers starting at R0. Pinning them
//     skips a Move pass at function entry.
//
// Algorithm:
//   1. Build an interval list (def_pos, last_use_pos, value) for every live
//      SSA value.
//   2. Sort by def_pos.
//   3. Walk the list. For each interval, expire active intervals whose
//      last_use_pos < current.def_pos (their registers go back to the free
//      pool). Allocate the smallest free register, or grow the file if none.
internal static class LinearScanAllocator
{
    public static RegisterAllocation Run(HirFunction func, BlockLayout layout, Linearization lin)
    {
        var alloc = new RegisterAllocation();

        // Pin entry-block live params to their slot index.
        var pinnedToSlot = new Dictionary<int, int>(); // InsnId.Value -> register
        if (func.EntryBlock.IsValid)
        {
            var entry = func[func.EntryBlock];
            for (var i = 0; i < entry.Params.Count; i++)
            {
                var p = entry.Params[i];
                var insn = func[p];
                if (insn.Kind != HirInsnKind.Param) continue;
                if (!lin.DefAt.ContainsKey(p.Value)) continue;
                pinnedToSlot[p.Value] = i;
            }
        }

        // Collect live ranges.
        var ranges = new List<Interval>();
        foreach (var (valueIdx, defPos) in lin.DefAt)
        {
            var endPos = lin.LastUse.TryGetValue(valueIdx, out var u) ? u : defPos;
            ranges.Add(new Interval(valueIdx, defPos, endPos));
        }
        ranges.Sort((a, b) => a.Start.CompareTo(b.Start));

        // Apply pinned assignments first; reserve those registers as in-use for
        // the entire pinned interval.
        var active = new List<Interval>(); // sorted by End ascending
        var freeRegs = new SortedSet<int>();
        var nextReg = 1; // R0 reserved for self
        foreach (var range in ranges)
        {
            ExpireBefore(active, freeRegs, range.Start);

            int reg;
            if (pinnedToSlot.TryGetValue(range.Value, out var slot))
            {
                reg = slot;
                if (reg >= nextReg) nextReg = reg + 1;
                // R0 might end up pinned to entry param 0 (self), which is fine.
                // Make sure we don't double-issue: if reg was in freeRegs (it
                // shouldn't be at this point but be defensive), remove it.
                freeRegs.Remove(reg);
            }
            else if (freeRegs.Count > 0)
            {
                reg = freeRegs.Min;
                freeRegs.Remove(reg);
            }
            else
            {
                reg = nextReg++;
            }
            alloc.Set(new InsnId(range.Value), reg);
            InsertSortedByEnd(active, range with { Reg = reg });
        }

        return alloc;
    }

    static void ExpireBefore(List<Interval> active, SortedSet<int> freeRegs, int pos)
    {
        // Remove intervals whose End < pos. Since active is sorted by End
        // ascending, we can scan from the front.
        var i = 0;
        while (i < active.Count && active[i].End < pos)
        {
            freeRegs.Add(active[i].Reg);
            active.RemoveAt(i);
        }
    }

    static void InsertSortedByEnd(List<Interval> active, Interval iv)
    {
        var lo = 0; var hi = active.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) >> 1;
            if (active[mid].End < iv.End) lo = mid + 1;
            else hi = mid;
        }
        active.Insert(lo, iv);
    }

    readonly record struct Interval(int Value, int Start, int End, int Reg = -1);
}
