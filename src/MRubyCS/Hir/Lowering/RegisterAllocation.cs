using System.Collections.Generic;

namespace MRubyCS.Hir.Lowering;

// Maps SSA values (HirInsn ids) to mruby register slots. The result of linear
// scan over a HirFunction; consumed by the BytecodeEmitter to place operands
// into the right register positions.
internal sealed class RegisterAllocation
{
    readonly Dictionary<int, int> regOf = new();

    /// <summary>Total registers used (RegisterVariableCount of the lowered Irep).</summary>
    public int RegisterCount { get; private set; }

    public bool TryGet(InsnId id, out int reg) => regOf.TryGetValue(id.Value, out reg);

    public int Get(InsnId id) =>
        regOf.TryGetValue(id.Value, out var reg)
            ? reg
            : throw new System.InvalidOperationException($"No register allocated for {id}");

    public void Set(InsnId id, int reg)
    {
        regOf[id.Value] = reg;
        if (reg + 1 > RegisterCount) RegisterCount = reg + 1;
    }
}
