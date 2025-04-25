using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Utf8StringInterpolation;

namespace MRubyCS.StdLib;

static class IntegerMembers
{
    [DoesNotReturn]
    internal static void RaiseIntegerOverflowError(MRubyState state, ReadOnlySpan<byte> message)
    {
        state.Raise(Names.RangeError, Utf8String.Format($"Integer overflow in {message}"));
    }

    [DoesNotReturn]
    internal static void RaiseDivideByZeroError(MRubyState state)
    {
        state.Raise(Names.RangeError, "divided by 0"u8);
    }

    [DoesNotReturn]
    internal static void RaiseIntegerNoConversionError(MRubyState state, MRubyValue value)
    {
        state.Raise(Names.TypeError, Utf8String.Format($"can't convert {state.TypeNameOf(value)} into Integer"));
    }


    internal static MRubyValue IntPow(MRubyState state, MRubyValue x, MRubyValue y)
    {
        var a = state.ToInteger(x);
        if (a == 0) return x;
        var other = state.GetArgumentAt(0);
        if (other.IsFloat)
        {
            return MRubyValue.From(Math.Pow(a, other.FloatValue));
        }
        var exp = state.ToInteger(other);
        if (exp < 0)
        {
            return MRubyValue.From(Math.Pow(a, other.FloatValue));
        }
        try
        {
            var result = 1L;
            while (true)
            {
                if ((exp & 1) != 0)
                {
                    result = checked(result * a);
                }
                exp >>= 1;
                if (exp == 0) break;
                a = checked(a * a);
            }
            return MRubyValue.From(result);
        }
        catch (OverflowException)
        {
            RaiseIntegerOverflowError(state, "power"u8);
            return default;
        }
    }

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod OpPow = new((state, self) => IntPow(state, self, state.GetArgumentAt(0)));

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod OpAdd = new((state, self) =>
    {
        var a = state.ToInteger(self);
        var other = state.GetArgumentAt(0);
        if (other.IsInteger)
        {
            try
            {
                return MRubyValue.From(checked(a + other.IntegerValue));
            }
            catch (OverflowException)
            {
                RaiseIntegerOverflowError(state, "addition"u8);
                return default;
            }
        }
        if (other.IsFloat)
        {
            return MRubyValue.From(a + other.FloatValue);
        }
        state.Raise(Names.TypeError, "non integer addition"u8);
        return default;
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod OpSub = new((state, self) =>
    {
        var a = state.ToInteger(self);
        var other = state.GetArgumentAt(0);
        if (other.IsInteger)
        {
            try
            {
                return MRubyValue.From(checked(a - other.IntegerValue));
            }
            catch (OverflowException)
            {
                RaiseIntegerOverflowError(state, "subtraction"u8);
                return default;
            }
        }
        if (other.IsFloat)
        {
            return MRubyValue.From(a - other.FloatValue);
        }
        state.Raise(Names.TypeError, "non integer subtraction"u8);
        return default;
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod OpMul = new((state, self) =>
    {
        var a = state.ToInteger(self);
        var other = state.GetArgumentAt(0);
        if (other.IsInteger)
        {
            try
            {
                return MRubyValue.From(checked(a * other.IntegerValue));
            }
            catch (OverflowException)
            {
                RaiseIntegerOverflowError(state, "multiplication"u8);
                return default;
            }
        }
        if (other.IsFloat)
        {
            return MRubyValue.From(a - other.FloatValue);
        }
        state.Raise(Names.TypeError, Utf8String.Format($"can't convert {state.TypeNameOf(other)} into Integer"));
        return default;
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod OpDiv = new((state, self) =>
    {
        var a = state.ToInteger(self);
        var other = state.GetArgumentAt(0);
        if (other.IsInteger)
        {
            if (other.IntegerValue == 0)
            {
                RaiseDivideByZeroError(state);
                return default;
            }
            return MRubyValue.From((a / other.IntegerValue));
        }
        if (other.IsFloat)
        {
            if (other.FloatValue == 0)
            {
                RaiseDivideByZeroError(state);
                return default;
            }
            return MRubyValue.From(a / other.FloatValue);
        }
        RaiseIntegerNoConversionError(state, other);
        return default;
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod Quo = new((state, self) =>
    {
        var other = state.GetArgumentAt(0);
        var f = state.ToFloat(other);
        if (f == 0)
        {
            RaiseDivideByZeroError(state);
            return default;
        }
        return MRubyValue.From((state.ToInteger(self) / f));
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod IntDiv = new((state, self) =>
    {
        var a = state.ToInteger(self);
        var other = state.GetArgumentAt(0);
        var b = state.ToInteger(other);
        if (b == 0)
        {
            RaiseDivideByZeroError(state);
            return default;
        }
        return MRubyValue.From(a / b);
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod FDiv = new((state, self) =>
    {
        var a = state.ToInteger(self);
        var other = state.GetArgumentAt(0);
        var b = state.ToFloat(other);
        if (b == 0)
        {
            RaiseDivideByZeroError(state);
            return default;
        }
        return MRubyValue.From(a / b);
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod OpAnd = new((state, self) =>
    {
        var a = state.ToInteger(self);
        var other = state.GetArgumentAt(0);
        var b = state.ToInteger(other);
        return MRubyValue.From(a & b);
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod OpOr = new((state, self) =>
    {
        var a = state.ToInteger(self);
        var other = state.GetArgumentAt(0);
        var b = state.ToInteger(other);
        return MRubyValue.From(a | b);
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod OpXor = new((state, self) =>
    {
        var a = state.ToInteger(self);
        var other = state.GetArgumentAt(0);
        var b = state.ToInteger(other);
        return MRubyValue.From(a ^ b);
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod OpLShift = new((state, self) =>
    {
        var a = state.ToInteger(self);
        var other = state.GetArgumentAt(0);
        var width = state.ToInteger(other);
        if (a == 0 || width == 0) return self;
        if (NumShift(state, a, width, out var num))
        {
            return MRubyValue.From(num);
        }
        RaiseIntegerOverflowError(state, "bit  shift"u8);
        return default;
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod OpRShift = new((state, self) =>
    {
        var a = state.ToInteger(self);
        var other = state.GetArgumentAt(0);
        var width = state.ToInteger(other);
        if (a == 0 || width == 0) return self;
        if (NumShift(state, a, -width, out var num))
        {
            return MRubyValue.From(num);
        }
        RaiseIntegerOverflowError(state, "bit  shift"u8);
        return default;
    });


    public static MRubyMethod ToS = new((state, self) =>
    {
        var basis = 10;
        if (state.GetArgumentCount() > 0)
        {
            basis = (int)state.GetArgumentAsIntegerAt(0);
        }

        return MRubyValue.From(state.StringifyInteger(self, basis));
    });

    public static MRubyMethod OpPlus = new((state, self) => { return MRubyValue.From(+self.IntegerValue); });

    public static MRubyMethod OpMinus = new((state, self) => { return MRubyValue.From(-self.IntegerValue); });

    public static MRubyMethod Abs = new((state, self) => { return MRubyValue.From(Math.Abs(self.IntegerValue)); });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod Mod = new((state, self) =>
    {
        var a = state.ToInteger(self);
        if (a == 0) return self;

        var other = state.GetArgumentAt(0);
        if (other.IsInteger)
        {
            var b = other.IntegerValue;
            if (b == 0)
            {
                state.Raise(Names.ZeroDivisionError, "divided by 0"u8);
            }

            var mod = a % b;
            if ((a < 0) != (b < 0) && mod != 0)
            {
                mod += b;
            }
            return MRubyValue.From(mod);
        }
        return FloatMembers.Mod.Invoke(state, self);
    });

    public static MRubyMethod Ceil = new((state, self) =>
    {
        var f = PrepareIntRounding(state, self);
        if (f.IsUndef)
        {
            return MRubyValue.From(0);
        }
        if (f.IsNil)
        {
            return MRubyValue.From((self.IntegerValue));
        }
        var a = state.ToInteger(self);
        var b = state.ToInteger(f);
        var c = a % b;
        var neg = a < 0;
        a -= c;
        if (!neg)
        {
            try
            {
                a = checked(a + b);
            }
            catch (OverflowException)
            {
                RaiseIntegerOverflowError(state, "ceiling"u8);
                return default;
            }
        }
        return MRubyValue.From(a);
    });

    public static MRubyMethod Floor = new((state, self) =>
    {
        var f = PrepareIntRounding(state, self);
        if (f.IsUndef)
        {
            return MRubyValue.From(0);
        }
        if (f.IsNil)
        {
            return MRubyValue.From((self.IntegerValue));
        }
        var a = state.ToInteger(self);
        var b = state.ToInteger(f);
        var c = a % b;
        var neg = a < 0;
        a -= c;
        if (!neg)
        {
            try
            {
                a = checked(a - b);
            }
            catch (OverflowException)
            {
                RaiseIntegerOverflowError(state, "floor"u8);
                return default;
            }
        }
        return MRubyValue.From(a);
    });

    public static MRubyMethod Round = new((state, self) =>
    {
        var f = PrepareIntRounding(state, self);
        if (f.IsUndef)
        {
            return MRubyValue.From(0);
        }
        if (f.IsNil)
        {
            return MRubyValue.From((self.IntegerValue));
        }
        var a = state.ToInteger(self);
        var b = state.ToInteger(f);
        var c = a % b;
        a -= c;

        try
        {
            if (c < 0)
            {
                c = -c;
                if (b / 2 < c)
                {
                    c = checked(a - b);
                }
                a = c;
            }
            else
            {
                if (b / 2 < c)
                {
                    c = checked(a + b);
                }
                a = c;
            }

            return MRubyValue.From(a);
        }
        catch (OverflowException)
        {
            RaiseIntegerOverflowError(state, "round"u8);
            return default;
        }
    });


    public static MRubyMethod Next = new((state, self) =>
    {
        try
        {
            return MRubyValue.From(checked(self.IntegerValue + 1));
        }
        catch (OverflowException)
        {
            RaiseIntegerOverflowError(state, "next"u8);
            return default;
        }
    });

    public static MRubyMethod Truncate = new((state, self) =>
    {
        var f = PrepareIntRounding(state, self);
        if (f.IsUndef)
        {
            return MRubyValue.From(0);
        }
        if (f.IsNil)
        {
            return MRubyValue.From((self.IntegerValue));
        }
        var a = state.ToInteger(self);
        var b = state.ToInteger(f);
        return MRubyValue.From(a - (a % b));
    });

    public static MRubyMethod Hash = new((state, self) =>
    {
        var n = state.ToInteger(self);
        return MRubyValue.From(RString.GetHashCode(MemoryMarshal.CreateSpan(ref Unsafe.As<long, byte>(ref n), sizeof(long))));
    });

    public static MRubyMethod DivMod = new((state, self) =>
    {
        var n = state.ToInteger(self);
        var other = state.GetArgumentAt(0);
        if (other.IsInteger)
        {
            IntDivMod(state, n, other.IntegerValue, out var div, out var mod);
            return MRubyValue.From(state.NewArray(MRubyValue.From(div), MRubyValue.From(mod)));
        }
        return FloatMembers.DivMod.Invoke(state, self);
    });

    public static MRubyMethod ToF = new((state, self) => MRubyValue.From((double)(state.ToInteger(self))));
    public static MRubyMethod ToI = new((state, self) => self);

    internal static bool NumShift(MRubyState state, long val, long width, out long num)
    {
        const int numericShiftWidthMax = 8 * sizeof(long) - 1;
        if (width < 0)
        {
            /* rshift */
            if (width == long.MinValue || -width >= (sizeof(long) - 1))
            {
                if (val < 0)
                {
                    num = -1;
                }
                else
                {
                    num = 0;
                }
            }
            else
            {
                num = val >> -(int)width;
            }
        }
        else if (val > 0)
        {
            if ((width > numericShiftWidthMax) ||
                (val > (long.MaxValue >> (int)width)))
            {
                num = default;
                return false;
            }
            num = val << (int)width;
        }
        else
        {
            if ((width > numericShiftWidthMax) ||
                (val < (long.MinValue >> (int)width)))
            {
                num = default;
                return false;
            }
            if (width == numericShiftWidthMax)
            {
                num = long.MinValue;
            }
            else
            {
                num = val * (1L << (int)width);
            }
        }
        return true;
    }

    internal static MRubyValue PrepareIntRounding(MRubyState state, MRubyValue x)
    {
        if (state.GetArgumentCount() <= 1)
        {
            return MRubyValue.Nil;
        }

        var other = state.GetArgumentAsFloatAt(0);
        if (-0.415241 * other - 0.125 > sizeof(long))
        {
            return MRubyValue.Undef;
        }
        return IntPow(state, MRubyValue.From(10), MRubyValue.From(-other));
    }

    internal static void IntDivMod(MRubyState state, long x, long y, out long divp, out long modp)
    {
        if (y == 0)
        {
            RaiseDivideByZeroError(state);
            Unsafe.SkipInit(out divp);
            Unsafe.SkipInit(out modp);
            return;
        }
        else if (x == int.MinValue && y == -1)
        {
            RaiseIntegerOverflowError(state, "division"u8);
            Unsafe.SkipInit(out divp);
            Unsafe.SkipInit(out modp);
            return;
        }
        else
        {
            long div = x / y;
            long mod = x - div * y;

            if ((x ^ y) < 0 && x != div * y)
            {
                mod += y;
                div -= 1;
            }
            divp = div;
            modp = mod;
        }
    }
}