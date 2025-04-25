using System;
using Utf8StringInterpolation;

namespace MRubyCS.StdLib;

static class FloatMembers
{
    public static MRubyMethod ToI = new((state, self) =>
    {
        var f = self.FloatValue;
        state.EnsureFloatValue(f);
        if (!IsFixableFloatValue(f))
        {
            state.Raise(Names.RangeError, state.NewString($"integer overflow in to_f"));
        }

        if (f > 0.0) return MRubyValue.From(Math.Floor(f));
        if (f < 0.0) return MRubyValue.From(Math.Ceiling(f));
        return MRubyValue.From((long)f);
    });

    public static MRubyMethod ToS = new((state, self) =>
    {
        var f = self.FloatValue;
        return MRubyValue.From(state.NewString(Utf8String.Format($"{f}")));
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
}