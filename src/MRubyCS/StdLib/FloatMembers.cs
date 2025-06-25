using System;
using System.Buffers.Text;

namespace MRubyCS.StdLib;

static class FloatMembers
{
    public static MRubyMethod ToI = new((state, self) =>
    {
        var f = self.FloatValue;
        EnsureExactValue(state, f);
        if (!IsFixableFloatValue(f))
        {
            state.Raise(Names.RangeError, "integer overflow in to_i"u8);
        }

        if (f > 0.0) return MRubyValue.From((long)Math.Floor(f));
        if (f < 0.0) return MRubyValue.From((long)Math.Ceiling(f));
        return MRubyValue.From((long)f);
    });

    public static MRubyMethod ToS = new((state, self) =>
    {
        var f = self.FloatValue;
        return MRubyValue.From(Format(state, f));
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod Mod = new((state, self) =>
    {
        var x = state.ToFloat(self);
        var y = state.GetArgumentAsFloatAt(0);

        if (double.IsNaN(y))
        {
            return MRubyValue.From(double.NaN);
        }

        if (y == 0.0)
        {
            state.Raise(Names.ZeroDivisionError, "divided by 0"u8);
        }

        if (double.IsInfinity(y) && !double.IsInfinity(x))
        {
            return MRubyValue.From(x);
        }
        return MRubyValue.From(x % y);
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod OpEq = new((state, self) =>
    {
        // Console.WriteLine("Float OpEq called");
        var x = self.FloatValue;
        var y = state.GetArgumentAt(0);
        if (y.IsInteger)
        {
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            return MRubyValue.From(x == (double)y.IntegerValue);
        }
        ;
        if (y.IsFloat)
        {
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            return MRubyValue.From(x == y.FloatValue);
        }
        return MRubyValue.False;
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod OpAdd = new((state, self) =>
    {
        var a = self.FloatValue;
        var arg = state.GetArgumentAt(0);
        var b = arg.VType switch
        {
            MRubyVType.Float => arg.FloatValue,
            _ => state.ToFloat(arg)
        };
        return MRubyValue.From(a + b);
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod OpSub = new((state, self) =>
    {
        var a = self.FloatValue;
        var arg = state.GetArgumentAt(0);
        var b = arg.VType switch
        {
            MRubyVType.Float => arg.FloatValue,
            _ => state.ToFloat(arg)
        };
        return MRubyValue.From(a - b);
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod OpMul = new((state, self) =>
    {
        var a = self.FloatValue;
        var arg = state.GetArgumentAt(0);
        var b = arg.VType switch
        {
            MRubyVType.Float => arg.FloatValue,
            _ => state.ToFloat(arg)
        };
        return MRubyValue.From(a * b);
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod OpDiv = new((state, self) =>
    {
        var a = self.FloatValue;
        var arg = state.GetArgumentAt(0);
        var b = arg.VType switch
        {
            MRubyVType.Float => arg.FloatValue,
            _ => state.ToFloat(arg)
        };
        return MRubyValue.From(a / b);
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod OpPow = new((state, self) =>
    {
        var a = self.FloatValue;
        var b = state.ToFloat(state.GetArgumentAt(0));
        return MRubyValue.From(Math.Pow(a, b));
    });

    public static MRubyMethod OpNeg = new((state, self) =>
    {
        return MRubyValue.From(-self.FloatValue);
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod OpLt = new((state, self) =>
    {
        var x = self.FloatValue;
        var arg = state.GetArgumentAt(0);

        double y;
        if (arg.IsFloat)
        {
            y = arg.FloatValue;
        }
        else if (arg.IsInteger)
        {
            y = (double)arg.IntegerValue;
        }
        else
        {
            return MRubyValue.False;
        }

        return MRubyValue.From(x < y);
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod OpLe = new((state, self) =>
    {
        var x = self.FloatValue;
        var arg = state.GetArgumentAt(0);

        double y;
        if (arg.IsFloat)
        {
            y = arg.FloatValue;
        }
        else if (arg.IsInteger)
        {
            y = (double)arg.IntegerValue;
        }
        else
        {
            return MRubyValue.False;
        }

        return MRubyValue.From(x <= y);
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod OpGt = new((state, self) =>
    {
        var x = self.FloatValue;
        var arg = state.GetArgumentAt(0);

        double y;
        if (arg.IsFloat)
        {
            y = arg.FloatValue;
        }
        else if (arg.IsInteger)
        {
            y = (double)arg.IntegerValue;
        }
        else
        {
            return MRubyValue.False;
        }

        return MRubyValue.From(x > y);
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod OpGe = new((state, self) =>
    {
        var x = self.FloatValue;
        var arg = state.GetArgumentAt(0);

        double y;
        if (arg.IsFloat)
        {
            y = arg.FloatValue;
        }
        else if (arg.IsInteger)
        {
            y = (double)arg.IntegerValue;
        }
        else
        {
            return MRubyValue.False;
        }

        return MRubyValue.From(x >= y);
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod OpAnd = new((state, self) =>
    {
        var v1 = ValueInt64(state, self);
        var v2 = ValueInt64(state, state.GetArgumentAt(0));
        return Int64Value(state, v1 & v2);
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod OpOr = new((state, self) =>
    {
        var v1 = ValueInt64(state, self);
        var v2 = ValueInt64(state, state.GetArgumentAt(0));
        return Int64Value(state, v1 | v2);
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod OpXor = new((state, self) =>
    {
        var v1 = ValueInt64(state, self);
        var v2 = ValueInt64(state, state.GetArgumentAt(0));
        return Int64Value(state, v1 ^ v2);
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod OpLshift = new((state, self) =>
    {
        var width = state.ToInteger(state.GetArgumentAt(0));
        return FloShift(state, self, width);
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod OpRshift = new((state, self) =>
    {
        var width = state.ToInteger(state.GetArgumentAt(0));
        if (width == long.MinValue) return FloShift(state, self, -64);
        return FloShift(state, self, -width);
    });

    public static MRubyMethod DivMod = new((state, self) =>
    {
        var x = state.ToFloat(self);
        var y = state.GetArgumentAt(0);
        MRubyValue a, b;
        FloatDivMod(state, x, state.ToFloat(y), out var div, out var mod);
        if (!IsFixableFloatValue(div))
        {
            a = MRubyValue.From(div);
        }
        else
        {
            a = MRubyValue.From((long)div);
        }

        b = MRubyValue.From(mod);
        return MRubyValue.From(state.NewArray(a, b));
    });

    public static MRubyMethod Abs = new((state, self) =>
    {
        var f = self.FloatValue;
        if (f < 0.0f)
        {
            return MRubyValue.From(-f);
        }
        return self;
    });

    public static MRubyMethod QNan = new((state, self) =>
    {
        return MRubyValue.From(double.IsNaN(self.FloatValue));
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod QEql = new((state, self) =>
    {
        var arg = state.GetArgumentAt(0);
        if (!arg.IsFloat)
        {
            return MRubyValue.False;
        }
        var x = self.FloatValue;
        var y = arg.FloatValue;
        return MRubyValue.From(x.Equals(y));
    });

    public static MRubyMethod QFinite = new((state, self) =>
    {
        var f = self.FloatValue;
        return MRubyValue.From(!double.IsInfinity(f) && !double.IsNaN(f));
    });

    public static MRubyMethod QInfinite = new((state, self) =>
    {
        var f = self.FloatValue;
        if (double.IsPositiveInfinity(f))
            return MRubyValue.From(1);
        if (double.IsNegativeInfinity(f))
            return MRubyValue.From(-1);
        return MRubyValue.Nil;
    });

    [MRubyMethod(OptionalArguments = 1)]
    public static MRubyMethod Ceil = new((state, self) =>
    {
        return FloatRounding(state, self, Math.Ceiling);
    });

    [MRubyMethod(OptionalArguments = 1)]
    public static MRubyMethod Floor = new((state, self) =>
    {
        return FloatRounding(state, self, Math.Floor);
    });

    public static MRubyMethod ToF = new((state, self) =>
    {
        return self;
    });

    public static MRubyMethod Hash = new((state, self) =>
    {
        var f = self.FloatValue;
        return MRubyValue.From(f.GetHashCode());
    });
    //
    [MRubyMethod(OptionalArguments = 1)]
    public static MRubyMethod Round = new((state, self) =>
    {
        var f = self.FloatValue;
        var ndigits = 0;

        var argc = state.GetArgumentCount();
        if (argc > 0)
        {
            var arg = state.GetArgumentAt(0);
            if (arg.IsInteger)
            {
                ndigits = (int)arg.IntegerValue;
            }
            else
            {
                state.Raise(Names.TypeError, "can't convert to integer"u8);
            }
        }

        if (ndigits == 0)
        {
            EnsureExactValue(state, f);
            var result = Math.Round(f, MidpointRounding.AwayFromZero);
            if (IsFixableFloatValue(result))
            {
                return MRubyValue.From((long)result);
            }
            return MRubyValue.From(result);
        }
        else if (ndigits > 0)
        {
            if (double.IsInfinity(f) || double.IsNaN(f))
            {
                return self;
            }
            if (ndigits > 15) ndigits = 15;
            var result = Math.Round(f, ndigits, MidpointRounding.AwayFromZero);
            return MRubyValue.From(result);
        }
        else
        {
            EnsureExactValue(state, f);
            var pow = Math.Pow(10, -ndigits);
            var result = Math.Round(f / pow, MidpointRounding.AwayFromZero) * pow;
            if (IsFixableFloatValue(result))
            {
                return MRubyValue.From((long)result);
            }
            return MRubyValue.From(result);
        }
    });
    //
    [MRubyMethod(OptionalArguments = 1)]
    public static MRubyMethod Truncate = new((state, self) =>
    {
        return FloatRounding(state, self, Math.Truncate);
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod Quo = new((state, self) =>
    {
        var x = self.FloatValue;
        var y = state.ToFloat(state.GetArgumentAt(0));
        return MRubyValue.From(x / y);
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod Div = new((state, self) =>
    {
        var x = self.FloatValue;
        var y = state.ToFloat(state.GetArgumentAt(0));
        if (y == 0.0)
        {
            state.Raise(Names.ZeroDivisionError, "divided by 0"u8);
        }
        var result = Math.Floor(x / y);
        if (IsFixableFloatValue(result))
        {
            return MRubyValue.From((long)result);
        }
        return MRubyValue.From(result);
    });

    public static MRubyMethod Inspect = new((state, self) =>
    {
        var f = self.FloatValue;
        return MRubyValue.From(Format(state, f));
    });

    public static MRubyMethod OpRev = new((state, self) =>
    {
        var v1 = ValueInt64(state, self);
        return Int64Value(state, ~v1);
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod OpCmp = new((state, self) =>
    {
        var x = self.FloatValue;
        var arg = state.GetArgumentAt(0);

        double y;
        if (arg.IsFloat)
        {
            y = arg.FloatValue;
        }
        else if (arg.IsInteger)
        {
            y = (double)arg.IntegerValue;
        }
        else
        {
            return MRubyValue.Nil;
        }

        if (double.IsNaN(x) || double.IsNaN(y))
        {
            return MRubyValue.Nil;
        }

        if (x < y) return MRubyValue.From(-1);
        if (x > y) return MRubyValue.From(1);
        return MRubyValue.From(0);
    });

    static void FloatDivMod(MRubyState state, double x, double y, out double divp, out double modp)
    {
        double div, mod;

        if (double.IsNaN(y))
        {
            /* y is NaN so all results are NaN */
            div = mod = y;
            goto exit;
        }
        if (y == 0.0)
        {
            IntegerMembers.RaiseDivideByZeroError(state);
        }
        if (double.IsInfinity(y) && !double.IsInfinity(x))
        {
            mod = x;
        }
        else
        {
            mod = (x % y);
        }
        if (double.IsInfinity(x) && !double.IsInfinity(y))
        {
            div = x;
        }
        else
        {
            div = (x - mod) / y;
            div = Math.Round(div);
        }
        if (div == 0) div = 0.0;
        if (mod == 0) mod = 0.0;
        if (y * mod < 0)
        {
            mod += y;
            div -= 1.0;
        }
        exit:
        modp = mod;
        divp = div;
    }

    static bool IsFixableFloatValue(double f) =>
        f is >= -9223372036854775808.0 and < 9223372036854775808.0;

    static RString Format(MRubyState state, double f)
    {
        if (double.IsPositiveInfinity(f))
        {
            return state.NewString("Infinity"u8);
        }
        if (double.IsNegativeInfinity(f))
        {
            return state.NewString("-Infinity"u8);
        }

        if (double.IsNaN(f))
        {
            return state.NewString("NaN"u8);
        }

        int bytesWritten;
        Span<byte> destination = stackalloc byte[8];
        while (!Utf8Formatter.TryFormat(f, destination, out bytesWritten))
        {
            destination = stackalloc byte[destination.Length * 2];
        }
        return state.NewString(destination.Slice(0, bytesWritten));
    }

    static long ValueInt64(MRubyState state, MRubyValue x)
    {
        switch (x.VType)
        {
            case MRubyVType.Integer:
                return x.IntegerValue;
            case MRubyVType.Float:
                var f = x.FloatValue;
                if (f is >= long.MinValue and <= long.MaxValue)
                    return (long)f;
                break;
        }
        state.Raise(Names.TypeError, "cannot convert to Integer"u8);
        return 0;
    }

    static MRubyValue Int64Value(MRubyState state, long v)
    {
        if (v >= int.MinValue && v <= int.MaxValue)
        {
            return MRubyValue.From(v);
        }
        state.Raise(Names.RangeError, "bit operation"u8);
        return MRubyValue.Nil;
    }

    static MRubyValue FloShift(MRubyState state, MRubyValue x, long width)
    {
        if (width == 0)
        {
            return x;
        }

        var f = x.FloatValue;
        double result;

        if (width > 0)
        {
            if (width >= 64) result = 0.0;
            else result = f * Math.Pow(2, width);
        }
        else
        {
            if (width <= -64) result = 0.0;
            else result = f / Math.Pow(2, -width);
        }

        if (IsFixableFloatValue(result))
        {
            return MRubyValue.From((long)result);
        }
        return MRubyValue.From(result);
    }

    static void EnsureExactValue(MRubyState state, double value)
    {
        if (double.IsNegativeInfinity(value))
        {
            state.Raise(Names.FloatDomainError, "-Infinity"u8);
        }
        if (double.IsPositiveInfinity(value))
        {
            state.Raise(Names.FloatDomainError, "Infinity"u8);
        }
        if (double.IsNaN(value))
        {
            state.Raise(Names.FloatDomainError, "NaN"u8);
        }
    }

    static MRubyValue FloatRounding(MRubyState state, MRubyValue num, Func<double, double> func)
    {
        var f = num.FloatValue;
        var ndigits = 0;
        const int fprec = 15;

        if (state.TryGetArgumentAt(0, out var arg))
        {
            if (!arg.IsInteger)
            {
                state.Raise(Names.TypeError, "can't convert to integer"u8);
            }
            ndigits = (int)arg.IntegerValue;
        }

        if (ndigits == 0)
        {
            if (double.IsInfinity(f) || double.IsNaN(f))
            {
                EnsureExactValue(state, f);
            }
            var result = func(f);
            if (IsFixableFloatValue(result))
            {
                return MRubyValue.From((long)result);
            }
            return MRubyValue.From(result);
        }
        else if (ndigits > 0)
        {
            if (double.IsInfinity(f) || double.IsNaN(f))
            {
                return num;
            }
            if (ndigits > fprec) ndigits = fprec;
            var pow = Math.Pow(10, ndigits);
            return MRubyValue.From(func(f * pow) / pow);
        }
        else
        {
            if (double.IsInfinity(f) || double.IsNaN(f))
            {
                EnsureExactValue(state, f);
            }
            if (ndigits < -fprec) ndigits = -fprec;
            var pow = Math.Pow(10, -ndigits);
            var result = func(f / pow) * pow;
            if (IsFixableFloatValue(result))
            {
                return MRubyValue.From((long)result);
            }
            return MRubyValue.From(result);
        }
    }
}
