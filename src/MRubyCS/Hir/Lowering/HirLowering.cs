namespace MRubyCS.Hir.Lowering;

// Top-level HIR -> mrb bytecode lowering. Phase H of the optimization plan.
//
// Pipeline (mirrors what a JIT backend would do, scaled down):
//   1. BlockLayout         : pick a linear order of basic blocks.
//   2. Linearization       : assign each program point a linear index.
//   3. LinearScanAllocator : assign every SSA value a register slot.
//   4. ComputeScratchSize  : figure out the widest call/op operand window.
//   5. BytecodeEmitter     : walk insns and emit operand-correct bytecode.
//
// Coverage progresses by phase: H1 = single-block + simple loads/return; H2
// = CFG / arithmetic / branches / phi parallel-copy; H3 = Send + Enter and
// recursive child Irep optimization.
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
        var scratchSize = ComputeScratchSize(func);
        var scratchBase = alloc.RegisterCount;

        if (scratchBase + scratchSize > byte.MaxValue + 1)
        {
            throw new System.NotSupportedException(
                $"Lowered Irep needs {scratchBase + scratchSize} registers; exceeds byte range");
        }

        var emitter = new BytecodeEmitter(func, layout, alloc, scratchBase, scratchBase + 1);
        var seq = emitter.Emit();
        // Reserve scratchSize registers above the allocator output. Always
        // leave at least 1 register (R0 = self) so the Irep is well-formed.
        var regCount = (ushort)System.Math.Max(scratchBase + scratchSize, 1);
        return new LoweredFunction { Sequence = seq, RegisterCount = regCount };
    }

    // Sizes the scratch zone that lives just above the allocator's output.
    // Two contributors:
    //   - Binary arithmetic / comparison: needs 2 (lhs, rhs).
    //   - Phi parallel-copy cycle-break: needs 1 (uses scratch[0]).
    //   - Send / Super: needs 1 (recv/self/result) + argc + kargc*2 + 1 (block
    //     slot, which the VM nil-clears even on block-less sends).
    // We take the per-insn max.
    static int ComputeScratchSize(HirFunction func)
    {
        var max = 2;
        foreach (var insn in func.Insns)
        {
            if (insn.Kind == HirInsnKind.Send || insn.Kind == HirInsnKind.Super)
            {
                var window = 2 + insn.Aux1 + insn.Aux2 * 2;
                if (window > max) max = window;
            }
        }
        return max;
    }
}
