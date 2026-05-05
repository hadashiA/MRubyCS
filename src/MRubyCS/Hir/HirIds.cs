using System;

namespace MRubyCS.Hir;

// Typed indices into the function's parallel arenas. Following ZJIT's pattern:
// instructions and blocks are stored in flat List<T>s on HirFunction, and all
// references are by id. Mutation is therefore "set fun.Insns[id] = ..." which
// auto-updates every consumer that holds the id.
public readonly struct InsnId(int value) : IEquatable<InsnId>
{
    public int Value { get; } = value;

    public static readonly InsnId Invalid = new(-1);
    public bool IsValid => Value >= 0;

    public bool Equals(InsnId other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is InsnId o && Equals(o);
    public override int GetHashCode() => Value;
    public static bool operator ==(InsnId a, InsnId b) => a.Value == b.Value;
    public static bool operator !=(InsnId a, InsnId b) => a.Value != b.Value;
    public override string ToString() => Value < 0 ? "v?" : $"v{Value}";
}

public readonly struct BlockId(int value) : IEquatable<BlockId>
{
    public static readonly BlockId Invalid = new(-1);

    public int Value { get; } = value;
    public bool IsValid => Value >= 0;

    public bool Equals(BlockId other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is BlockId o && Equals(o);
    public override int GetHashCode() => Value;
    public static bool operator ==(BlockId a, BlockId b) => a.Value == b.Value;
    public static bool operator !=(BlockId a, BlockId b) => a.Value != b.Value;
    public override string ToString() => Value < 0 ? "bb?" : $"bb{Value}";
}
