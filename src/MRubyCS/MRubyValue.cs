using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using Utf8StringInterpolation;
#if NET8_0_OR_GREATER
using MRubyCS.Internals;
#endif

namespace MRubyCS;

public enum MRubyVType
{
    Nil = 0,
    False,
    True,
    Symbol,
    Undef,
    Free,
    Float,
    Integer,
    CPtr,
    Object,
    Class,
    Module,
    IClass, // Include class
    SClass, // Singleton class
    Proc,
    Array,
    Hash,
    String,
    Range,
    Exception,
    Env,
    CSharpData,
    Fiber,
    Struct,
    Break,
    Complex,
    Rational,
    BigInt,
}

public static class MRubyVTypeExtensions
{
    public static ReadOnlySpan<byte> ToUtf8String(this MRubyVType vType)
    {
        return Utf8String.Format($"{vType}");
    }

    public static bool IsClass(this MRubyVType vType) => vType is MRubyVType.Class or MRubyVType.SClass or MRubyVType.Module;
}

public readonly struct MRubyValue : IEquatable<MRubyValue>
{
    public static MRubyValue Nil => default;

#if NET8_0_OR_GREATER
    // --- TypeObjectUnion path (NET8+) ---

    public static MRubyValue False => new(new TypeObjectUnion(MRubyVType.False), 0);
    public static MRubyValue True => new(new TypeObjectUnion(MRubyVType.True), 0);
    public static MRubyValue Undef => new(new TypeObjectUnion(MRubyVType.Undef), 0);

    internal static readonly long FixnumMin = long.MinValue;
    internal static readonly long FixnumMax = long.MaxValue;

    readonly TypeObjectUnion union;
    readonly long bits;

    public RObject? Object
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => union.Object;
    }

    public MRubyVType VType
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => union.IsObject ? union.RawObject.VType : union.TypeValue;
    }

    public bool IsNil
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => union.RawValue == 0;
    }

    public bool IsFalse
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => union.RawValue == (nint)MRubyVType.False;
    }

    public bool IsTrue
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => union.RawValue == (nint)MRubyVType.True;
    }

    public bool IsUndef
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => union.RawValue == (nint)MRubyVType.Undef;
    }

    public bool IsSymbol
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => union.RawValue == (nint)MRubyVType.Symbol;
    }

    public bool IsFixnum
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => union.RawValue == (nint)MRubyVType.Integer;
    }

    public bool IsObject
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => union.IsObject;
    }

    public bool IsImmediate
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => union.IsType;
    }

    public bool Truthy
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => union.RawValue > 1; // Nil=0, False=1
    }

    public bool Falsy
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => union.RawValue <= 1; // Nil=0, False=1
    }

    public bool IsInteger
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => IsFixnum || (union.IsObject && union.RawObject.VType == MRubyVType.Integer);
    }

    public bool IsFloat
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => union.RawValue == (nint)MRubyVType.Float;
    }

    internal bool IsNumeric
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => IsFixnum || IsFloat || (union.IsObject && union.RawObject.VType == MRubyVType.Integer);
    }

    public bool IsBreak
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => union.IsObject && union.RawObject.VType == MRubyVType.Break;
    }

    public bool IsProc
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => union.IsObject && union.RawObject.VType == MRubyVType.Proc;
    }

    public bool IsClass
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => union.IsObject && union.RawObject.VType is MRubyVType.Class or MRubyVType.SClass or MRubyVType.Module;
    }

    public bool IsNamespace
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => union.IsObject && union.RawObject.VType is MRubyVType.Class or MRubyVType.Module;
    }

    public bool BoolValue
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Truthy;
    }

    public long FixnumValue
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => bits;
    }

    public Symbol SymbolValue
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new((uint)bits);
    }

    public long IntegerValue
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => union.IsType ? bits : As<RInteger>().Value;
    }

    public double FloatValue
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            var fbits = bits;
            return Unsafe.As<long, double>(ref fbits);
        }
    }

    public unsafe long ObjectId
    {
        get
        {
            if (union.IsObject)
            {
                var obj = union.RawObject;
                return (nint)Unsafe.AsPointer(ref obj);
            }
            if (IsInteger) return IntegerValue;
            if (IsFloat) return (long)FloatValue;
            return bits;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MRubyValue(bool value)
    {
        union = new TypeObjectUnion(value ? MRubyVType.True : MRubyVType.False);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MRubyValue(int value)
    {
        union = new TypeObjectUnion(MRubyVType.Integer);
        bits = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MRubyValue(long value)
    {
        union = new TypeObjectUnion(MRubyVType.Integer);
        bits = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MRubyValue(Symbol symbol)
    {
        union = new TypeObjectUnion(MRubyVType.Symbol);
        bits = symbol.Value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MRubyValue(double value)
    {
        union = new TypeObjectUnion(MRubyVType.Float);
        bits = Unsafe.As<double, long>(ref value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MRubyValue(RObject value)
    {
        union = new TypeObjectUnion(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    MRubyValue(TypeObjectUnion union, long bits)
    {
        this.union = union;
        this.bits = bits;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(MRubyValue other) => union == other.union && bits == other.bits;

    public override int GetHashCode()
    {
        if (union.IsObject) return union.RawObject.GetHashCode();
        return bits.GetHashCode();
    }

#else
    // --- Bit-manipulation path (netstandard2.1 / Unity) ---

    public static MRubyValue False => new(FalseBits, null);
    public static MRubyValue True => new(TrueBits, null);
    public static MRubyValue Undef => new(UndefBits, null);

    const long FalseBits = 0b0100;
    const long TrueBits = 0b1100;
    const long UndefBits = 0b0001_0100;
    const int SymbolShift = 32;

    internal static readonly long FixnumMin = long.MinValue >> 1;
    internal static readonly long FixnumMax = long.MaxValue >> 1;

    readonly long bits;

    public RObject? Object
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get;
    }

    public MRubyVType VType => this switch
    {
        { Object: { } obj } => obj.VType,
        { IsTrue: true } => MRubyVType.True,
        { IsFalse: true } => MRubyVType.False,
        { IsUndef: true } => MRubyVType.Undef,
        { IsSymbol: true } => MRubyVType.Symbol,
        { IsFixnum: true } => MRubyVType.Integer,
        { IsFloat: true } => MRubyVType.Float,
        _ => default
    };

    public bool IsNil
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => bits == 0 && Object == null;
    }

    public bool IsFalse
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => bits == FalseBits;
    }

    public bool IsTrue
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => bits == TrueBits;
    }

    public bool IsUndef
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => bits == UndefBits;
    }

    public bool IsSymbol
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (bits & 0b1_1111) == 0b1_1100;
    }

    public bool IsFixnum
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (bits & 1) == 1;
    }

    public bool IsObject
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Object != null;
    }

    public bool IsImmediate
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Object == null;
    }

    public bool Truthy
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => !IsFalse && !IsNil;
    }

    public bool Falsy
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => IsNil || IsFalse;
    }

    public bool IsInteger
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => IsFixnum || Object?.VType == MRubyVType.Integer;
    }

    public bool IsFloat
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (bits & 0b11) == 0b10;
    }

    internal bool IsNumeric
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => IsFixnum || IsFloat || Object?.VType == MRubyVType.Integer;
    }

    public bool IsBreak
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Object?.VType == MRubyVType.Break;
    }

    public bool IsProc
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Object?.VType == MRubyVType.Proc;
    }

    public bool IsClass
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Object?.VType is MRubyVType.Class or MRubyVType.SClass or MRubyVType.Module;
    }

    public bool IsNamespace
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Object?.VType is MRubyVType.Class or MRubyVType.Module;
    }

    public bool BoolValue
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (bits & ~False.bits) != 0;
    }

    public long FixnumValue
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => bits >> 1;
    }

    public Symbol SymbolValue
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new((uint)(bits >> SymbolShift));
    }

    public long IntegerValue
    {
        get
        {
            if (Object?.VType == MRubyVType.Integer)
            {
                return As<RInteger>().Value;
            }
            return bits >> 1;
        }
    }

    public double FloatValue
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            var fbits = bits & ~0b11;
            return Unsafe.As<long, double>(ref fbits);
        }
    }

    public unsafe long ObjectId
    {
        get
        {
            if (Object is { } obj)
            {
                return (nint)Unsafe.AsPointer(ref obj);
            }
            if (IsInteger) return IntegerValue;
            if (IsFloat) return (long)FloatValue;
            return bits;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MRubyValue(bool value)
    {
        bits = value ? TrueBits : FalseBits;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MRubyValue(int value)
    {
        bits = (value << 1) | 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MRubyValue(long value)
    {
        if (value > FixnumMax || value < FixnumMin)
        {
            ThrowIntegerOveflow(value);
        }
        bits = (value << 1) | 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MRubyValue(Symbol symbol)
    {
        bits = ((long)symbol.Value << SymbolShift) | 0b1_1100;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MRubyValue(double value)
    {
        var n = Unsafe.As<double, long>(ref value);
        bits = (n & ~0b11) | 0b10;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MRubyValue(RObject value)
    {
        Object = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    MRubyValue(long bits, RObject? obj)
    {
        this.bits = bits;
        Object = obj;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(MRubyValue other) => bits == other.bits &&
                                            Object == other.Object;

    public override int GetHashCode()
    {
        return Object?.GetHashCode() ?? bits.GetHashCode();
    }

#endif

    // --- Common API ---

    // Implicit conversion operators
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator MRubyValue(bool value) => new(value);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator MRubyValue(RObject obj) => new(obj);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator MRubyValue(long value) => new(value);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator MRubyValue(int value) => new(value);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator MRubyValue(Symbol symbol) => new(symbol);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator MRubyValue(double value) => new(value);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator MRubyValue(float value) => new(value);

    // Obsolete factory methods (kept for backward compatibility)
    [Obsolete("Use constructor instead: new MRubyValue(value)")]
    public static MRubyValue From(bool value) => new(value);

    [Obsolete("Use constructor instead: new MRubyValue(obj)")]
    public static MRubyValue From(RObject obj) => new(obj);

    [Obsolete("Use constructor instead: new MRubyValue(value)")]
    public static MRubyValue From(long value) => new(value);

    [Obsolete("Use constructor instead: new MRubyValue(symbol)")]
    public static MRubyValue From(Symbol symbol) => new(symbol);

    [Obsolete("Use constructor instead: new MRubyValue(value)")]
    public static MRubyValue From(double value) => new(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T As<T>() where T : RObject => (T)Object!;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(MRubyValue a, MRubyValue b) => a.Equals(b);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(MRubyValue a, MRubyValue b) => !a.Equals(b);

    public override bool Equals(object? obj)
    {
        return obj is MRubyValue other && Equals(other);
    }

    public override string ToString()
    {
        if (Object is { } x) return x.ToString()!;
        if (IsNil) return "nil";

        return VType switch
        {
            MRubyVType.False => "false",
            MRubyVType.True => "true",
            MRubyVType.Undef => "undef",
            MRubyVType.Symbol => SymbolValue.ToString()!,
            MRubyVType.Float => FloatValue.ToString(CultureInfo.InvariantCulture),
            MRubyVType.Integer => IntegerValue.ToString(CultureInfo.InvariantCulture),
            _ => VType.ToString()
        };
    }

    static void ThrowIntegerOveflow(long intValue)
    {
        throw new ArgumentException(
            $"MRubyValue integers only support values up to 63 bits ({FixnumMax}). To hold larger values, use MRubyState.NewInteger.");
    }
}
