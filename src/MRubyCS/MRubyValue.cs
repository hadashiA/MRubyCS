using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using Utf8StringInterpolation;

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
    Istruct,
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

// mrb_value representation:
//
// 64-bit word with inline float:
//   nil   : ...0000 0000 (all bits are 0)
//   false : ...0000 0100 (mrb_fixnum(v) != 0)
//   true  : ...0000 1100
//   undef : ...0001 0100
//   symbol: ...0001 1100 (use only upper 32-bit as symbol value with MRB_64BIT)
//   fixnum: ...IIII III1
//   float : ...FFFF FF10 (51 bit significands; require MRB_64BIT)
//   object: ...PPPP P000
public readonly struct MRubyValue : IEquatable<MRubyValue>
{
    public static MRubyValue Nil => default;
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

    public bool IsNil => bits == 0 && Object == null;
    public bool IsFalse => bits == FalseBits;
    public bool IsTrue => bits == TrueBits;
    public bool IsUndef => bits == UndefBits;
    public bool IsSymbol => (bits & 0b1_1111) == 0b1_1100;
    public bool IsFixnum => (bits & 1) == 1;
    public bool IsObject => Object != null;
    public bool IsImmediate => Object == null;

    public bool Truthy => !IsFalse && !IsNil;
    public bool Falsy => IsNil || IsFalse;

    public bool IsInteger => IsFixnum ||
                             Object?.VType == MRubyVType.Integer;
    public bool IsFloat => (bits & 0b11) == 0b10;
    internal bool IsNumeric => IsFixnum || IsFloat || Object?.VType == MRubyVType.Integer;
    public bool IsBreak => Object?.VType == MRubyVType.Break;
    public bool IsProc => Object?.VType == MRubyVType.Proc;
    public bool IsClass => Object?.VType is MRubyVType.Class or MRubyVType.SClass or  MRubyVType.Module;
    public bool IsNamespace => Object?.VType is MRubyVType.Class or MRubyVType.Module;

    public bool BoolValue => (bits & ~False.bits) != 0;
    public long FixnumValue => bits >> 1;
    public Symbol SymbolValue => new((uint)(bits >> SymbolShift));

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

    public MRubyValue(bool value)
    {
        bits = value ? TrueBits : FalseBits;
    }

    public MRubyValue(int value)
    {
        bits = (value << 1) | 1;
    }

    public MRubyValue(long value)
    {
        if (value > FixnumMax || value < FixnumMin)
        {
            ThrowIntegerOveflow(value);
        }
        bits = (value << 1) | 1;
    }

    public MRubyValue(Symbol symbol)
    {
        bits = ((long)symbol.Value << SymbolShift) | 0b1_1100;
    }

    public MRubyValue(double value)
    {
        var n = Unsafe.As<double, long>(ref value);
        bits = (n & ~0b11) | 0b10;
    }

    public MRubyValue(RObject value)
    {
        Object = value;
    }

    MRubyValue(long bits, RObject? obj)
    {
        this.bits = bits;
        Object = obj;
    }

    // Implicit conversion operators
    public static implicit operator MRubyValue(bool value) => new(value);
    public static implicit operator MRubyValue(RObject obj) => new(obj);
    public static implicit operator MRubyValue(long value) => new(value);
    public static implicit operator MRubyValue(int value) => new(value);
    public static implicit operator MRubyValue(Symbol symbol) => new(symbol);
    public static implicit operator MRubyValue(double value) => new(value);
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

    public T As<T>() where T : RObject => (T)Object!;

    public bool Equals(MRubyValue other) => bits == other.bits &&
                                            Object == other.Object;

    public static bool operator ==(MRubyValue a, MRubyValue b) => a.Equals(b);
    public static bool operator !=(MRubyValue a, MRubyValue b) => !a.Equals(b);

    public override bool Equals(object? obj)
    {
        return obj is MRubyValue other && Equals(other);
    }

    public override int GetHashCode()
    {
        return Object?.GetHashCode() ?? bits.GetHashCode();
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