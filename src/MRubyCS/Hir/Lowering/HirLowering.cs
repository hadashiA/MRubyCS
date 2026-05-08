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
    public static LoweredFunction Lower(HirFunction func)
    {
        var layout = BlockLayout.Compute(func);
        var lin = Linearization.Compute(func, layout);
        var alloc = LinearScanAllocator.Run(func, layout, lin);
        var emitter = new BytecodeEmitter(func, layout, alloc);
        var seq = emitter.Emit();
        // Always reserve at least 1 register (R0 = self) even if the allocator
        // didn't assign anything (e.g. a function whose body is just `Stop`).
        var regCount = (ushort)System.Math.Max(alloc.RegisterCount, 1);
        return new LoweredFunction { Sequence = seq, RegisterCount = regCount };
    }
}
