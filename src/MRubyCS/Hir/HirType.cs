using System;

namespace MRubyCS.Hir;

// Bits identifying disjoint type categories. A HirType.Bits value is the union
// of categories an SSA value might be at runtime. Combined with Spec, we get
// finer-grained refinements (exact class, exact constant, etc.).
//
// Layout was chosen so that lattice ops are bit-or / bit-and; subtype is
// bit-and equality. Keep this enum stable — passes encode bit patterns.
[Flags]
public enum HirTypeBits : ulong
{
    None     = 0,
    Nil      = 1ul << 0,
    True     = 1ul << 1,
    False    = 1ul << 2,
    Bool     = True | False,
    Integer  = 1ul << 3,
    Float    = 1ul << 4,
    Numeric  = Integer | Float,
    Symbol   = 1ul << 5,
    String   = 1ul << 6,
    Array    = 1ul << 7,
    Hash     = 1ul << 8,
    Proc     = 1ul << 9,
    Range    = 1ul << 10,
    ClassObj = 1ul << 11,
    ModuleObj = 1ul << 12,
    Exception = 1ul << 13,
    // Catch-all for any heap object not in a more specific category. RObject
    // subtypes that we haven't broken out yet land here.
    OtherObject = 1ul << 14,

    AllImmediates = Nil | True | False | Integer | Float | Symbol,
    AllHeap = String | Array | Hash | Proc | Range | ClassObj | ModuleObj | Exception | OtherObject,
    Any = AllImmediates | AllHeap,
}

// Refinement on top of Bits. Most analyses care about Bits; specialization
// fires constant folding, type guards, and direct dispatch.
public enum HirSpec : byte
{
    None,
    ExactClass,    // Holds a value whose Class is exactly KnownClass (no subclass)
    ClassOrSub,    // Holds a value whose Class is KnownClass or a subclass
    ConstInt,      // Holds an exact integer literal (Aux: IntValue)
    ConstFloat,    // Holds an exact float literal (Aux: FloatValue)
    ConstSym,      // Holds an exact symbol (Aux: SymValue)
    ConstValue,    // Holds an exact MRubyValue (Aux: BoxedValue)
}

public readonly struct HirType : IEquatable<HirType>
{
    public HirTypeBits Bits { get; }
    public HirSpec Spec { get; }

    // Spec-dependent payload. At most one of these is meaningful at a time.
    public RClass? KnownClass { get; }
    public long IntValue { get; }
    public double FloatValue { get; }
    public Symbol SymValue { get; }
    public MRubyValue BoxedValue { get; }

    HirType(HirTypeBits bits, HirSpec spec, RClass? cls, long iv, double fv, Symbol sym, MRubyValue boxed)
    {
        Bits = bits;
        Spec = spec;
        KnownClass = cls;
        IntValue = iv;
        FloatValue = fv;
        SymValue = sym;
        BoxedValue = boxed;
    }

    // --- Constants (lattice top/bottom + common "categories") ---

    public static readonly HirType Empty = default;
    public static readonly HirType Any = new(HirTypeBits.Any, HirSpec.None, null, 0, 0, default, default);
    public static readonly HirType Nil = new(HirTypeBits.Nil, HirSpec.None, null, 0, 0, default, default);
    public static readonly HirType True = new(HirTypeBits.True, HirSpec.None, null, 0, 0, default, default);
    public static readonly HirType False = new(HirTypeBits.False, HirSpec.None, null, 0, 0, default, default);
    public static readonly HirType Bool = new(HirTypeBits.Bool, HirSpec.None, null, 0, 0, default, default);
    public static readonly HirType Integer = new(HirTypeBits.Integer, HirSpec.None, null, 0, 0, default, default);
    public static readonly HirType Float = new(HirTypeBits.Float, HirSpec.None, null, 0, 0, default, default);
    public static readonly HirType Symbol = new(HirTypeBits.Symbol, HirSpec.None, null, 0, 0, default, default);
    public static readonly HirType String = new(HirTypeBits.String, HirSpec.None, null, 0, 0, default, default);
    public static readonly HirType Array = new(HirTypeBits.Array, HirSpec.None, null, 0, 0, default, default);
    public static readonly HirType Hash = new(HirTypeBits.Hash, HirSpec.None, null, 0, 0, default, default);
    public static readonly HirType Proc = new(HirTypeBits.Proc, HirSpec.None, null, 0, 0, default, default);
    public static readonly HirType Range = new(HirTypeBits.Range, HirSpec.None, null, 0, 0, default, default);

    public bool IsEmpty => Bits == HirTypeBits.None;

    // --- Refined factories ---

    public static HirType ConstInt(long value) =>
        new(HirTypeBits.Integer, HirSpec.ConstInt, null, value, 0, default, default);

    public static HirType ConstFloat(double value) =>
        new(HirTypeBits.Float, HirSpec.ConstFloat, null, 0, value, default, default);

    public static HirType ConstSym(Symbol sym) =>
        new(HirTypeBits.Symbol, HirSpec.ConstSym, null, 0, 0, sym, default);

    public static HirType ConstValue(MRubyValue v, HirTypeBits bits) =>
        new(bits, HirSpec.ConstValue, null, 0, 0, default, v);

    public static HirType ExactClassOf(RClass cls, HirTypeBits bits) =>
        new(bits, HirSpec.ExactClass, cls, 0, 0, default, default);

    public static HirType ClassOrSubclassOf(RClass cls, HirTypeBits bits) =>
        new(bits, HirSpec.ClassOrSub, cls, 0, 0, default, default);

    // --- Lattice operations ---

    // Union (least upper bound): widens. Drops Spec when the two specializations
    // disagree because we can no longer say "is exactly X".
    public HirType Union(HirType other)
    {
        // Empty (bottom) is the lattice identity for Union.
        if (IsEmpty) return other;
        if (other.IsEmpty) return this;

        var bits = Bits | other.Bits;
        if (Equals(other)) return this;
        // Different refinements on the two sides; collapse to bits-only.
        // (Future: keep ClassOrSub when both refine to the same hierarchy.)
        return new HirType(bits, HirSpec.None, null, 0, 0, default, default);
    }

    // Intersection (greatest lower bound): narrows.
    public HirType Intersect(HirType other)
    {
        var bits = Bits & other.Bits;
        if (bits == HirTypeBits.None) return Empty;
        if (Spec != HirSpec.None) return WithBits(bits);
        if (other.Spec != HirSpec.None) return other.WithBits(bits);
        return new HirType(bits, HirSpec.None, null, 0, 0, default, default);
    }

    HirType WithBits(HirTypeBits bits) => new(bits, Spec, KnownClass, IntValue, FloatValue, SymValue, BoxedValue);

    public bool IsSubtypeOf(HirType other) => (Bits & other.Bits) == Bits;

    // --- Equality ---

    public bool Equals(HirType other) =>
        Bits == other.Bits && Spec == other.Spec &&
        ReferenceEquals(KnownClass, other.KnownClass) &&
        IntValue == other.IntValue && FloatValue == other.FloatValue &&
        SymValue == other.SymValue && BoxedValue == other.BoxedValue;

    public override bool Equals(object? obj) => obj is HirType t && Equals(t);
    public override int GetHashCode() => HashCode.Combine((ulong)Bits, (byte)Spec, KnownClass);
    public static bool operator ==(HirType a, HirType b) => a.Equals(b);
    public static bool operator !=(HirType a, HirType b) => !a.Equals(b);

    // --- Display ---

    public override string ToString()
    {
        if (IsEmpty) return "Empty";
        if (Bits == HirTypeBits.Any && Spec == HirSpec.None) return "Any";
        var bitName = Spec switch
        {
            HirSpec.ConstInt => $"Integer={IntValue}",
            HirSpec.ConstFloat => $"Float={FloatValue}",
            HirSpec.ConstSym => $"Symbol(#{SymValue.Value})",
            HirSpec.ConstValue => $"Const({BoxedValue})",
            HirSpec.ExactClass => $"{Bits}={KnownClass?.ToString() ?? "?"}",
            HirSpec.ClassOrSub => $"{Bits}<:{KnownClass?.ToString() ?? "?"}",
            _ => Bits.ToString(),
        };
        return bitName;
    }
}
