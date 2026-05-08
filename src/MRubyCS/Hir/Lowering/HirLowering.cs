namespace MRubyCS.Hir.Lowering;

// Top-level HIR -> mrb bytecode lowering. Phase H of the optimization plan.
//
// Pipeline (mirrors what a JIT backend would do, scaled down):
//   1. BlockLayout  : pick a linear order of basic blocks.
//   2. Linearization: assign each program point a linear index for live ranges.
//   3. LinearScanAllocator: assign every SSA value a register slot.
//   4. BytecodeEmitter: walk insns and emit operand-correct bytecode bytes.
//
// Coverage: H1 supports a single-block, edge-free function with the simplest
// load / move / return / stop insns — enough to round-trip a trivial program
// and validate the plumbing. H2+ adds CFG, phi resolution, arithmetic, and
// progressively more insn kinds.
public sealed class LoweredFunction
{
    public byte[] Sequence { get; init; } = [];
    public ushort RegisterCount { get; init; }
}

public static class HirLowering
{
    // Two consecutive scratch registers above the linear-scan output:
    //   * scratch0: lhs of binary ops (mruby's `R[a]`); also break-cycle temp
    //               for parallel-copy phi resolution.
    //   * scratch1: rhs of binary ops (mruby's `R[a+1]`).
    //
    // These are clobbered freely by every emit; the upstream IR may not assume
    // anything about their values across insns. Reserved as the highest two
    // registers in the lowered Irep.
    const int ScratchSize = 2;

    public static LoweredFunction Lower(HirFunction func)
    {
        var layout = BlockLayout.Compute(func);
        var lin = Linearization.Compute(func, layout);
        var alloc = LinearScanAllocator.Run(func, layout, lin);
        var scratch0 = alloc.RegisterCount;
        var scratch1 = scratch0 + 1;
        var emitter = new BytecodeEmitter(func, layout, alloc, scratch0, scratch1);
        var seq = emitter.Emit();
        // Reserve ScratchSize registers above the allocator's output; ensure
        // at least 1 register (R0 = self) is always present.
        var regCount = (ushort)System.Math.Max(alloc.RegisterCount + ScratchSize, 1);
        return new LoweredFunction { Sequence = seq, RegisterCount = regCount };
    }
}
