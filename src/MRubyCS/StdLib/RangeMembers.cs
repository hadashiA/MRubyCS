using System.Runtime.CompilerServices;

namespace MRubyCS.StdLib;

static class RangeMembers
{
    public static MRubyMethod Begin = new((state, self) =>
    {
        return self.As<RRange>().Begin;
    });

    public static MRubyMethod End = new((state, self) =>
    {
        return self.As<RRange>().End;
    });

    public static MRubyMethod ExcludeEnd = new((state, self) =>
    {
        return MRubyValue.From(self.As<RRange>().Exclusive);
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod OpEq = new((state, self) =>
    {
        var arg0 = state.GetArgumentAt(0);
        if (self == arg0) return MRubyValue.True;

        var range = self.As<RRange>();
        if (arg0.Object is not RRange rangeOther)
        {
            return MRubyValue.False;
        }
        return MRubyValue.From(range.Begin == rangeOther.Begin &&
                               range.End == rangeOther.End &&
                               range.Exclusive == rangeOther.Exclusive);
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod IsInclude = new((state, self) =>
    {
        var range = self.As<RRange>();
        var value = state.GetArgumentAt(0);

        if (range.Begin.IsNil)
        {
            var result = range.Exclusive
                // end > value
                ? state.ValueCompare(range.End, value) == 1
                // end >= value
                : state.ValueCompare(range.End, value) is 0 or 1;
            return MRubyValue.From(result);
        }

        // begin <= value
        if (state.ValueCompare(range.Begin, value) is 0 or -1)
        {
            if (range.End.IsNil)
            {
                return MRubyValue.True;
            }

            var result = range.Exclusive
                // end > value
                ? state.ValueCompare(range.End, value) == 1
                // end >= value
                : state.ValueCompare(range.End, value) is 0 or 1;
            return MRubyValue.From(result);
        }
        return MRubyValue.False;
    });

    public static MRubyMethod ToS = new((state, self) =>
    {
        var range = self.As<RRange>();
        var b = state.Stringify(range.Begin);
        var e = state.Stringify(range.End);

        var result = range.Exclusive
            ? state.NewString($"{b}...{e}")
            : state.NewString($"{b}..{e}");
        return MRubyValue.From(result);
    });

    public static MRubyMethod Inspect = new((state, self) =>
    {
        var range = self.As<RRange>();
        var result = state.NewString(6);
        if (!range.Begin.IsNil)
        {
            var b = state.InspectObject(range.Begin);
            result.Concat(b);
        }
        result.Concat(range.Exclusive ? "..."u8 : ".."u8);
        if (!range.End.IsNil)
        {
            var e = state.InspectObject(range.End);
            result.Concat(e);
        }
        return MRubyValue.From(result);
    });

    public static MRubyMethod InternalNumToA = new((state, self) =>
    {
        var range = self.As<RRange>();
        if (range.End.IsNil)
        {
            state.Raise(Names.RangeError, "cannot convert endless range to an array"u8);
        }

        if (range.Begin.IsInteger)
        {
            if (range.End.IsInteger)
            {
                var a = range.Begin.IntegerValue;
                var b = range.End.IntegerValue;
                var len = b - a;
                if (!range.Exclusive) len++;

                var array = state.NewArray((int)len);
                array.MakeModifiable((int)len, true);
                for (var i = 0; i < len; i++)
                {
                    array[i] = MRubyValue.From(a + i);
                }

                return MRubyValue.From(array);
            }

            if (range.End.IsFloat)
            {
                var a = (float)range.Begin.IntegerValue;
                var b = range.End.FloatValue;
                if (a > b)
                {
                    return MRubyValue.From(state.NewArray(0));
                }

                var array = state.NewArray((int)(b - a) + 1);
                var i = 0;
                if (range.Exclusive)
                {
                    while (a < b)
                    {
                        array[i++] = MRubyValue.From((int)a);
                        a += 1f;
                    }
                }
                else
                {
                    while (a <= b)
                    {
                        array[i++] = MRubyValue.From((int)a);
                        a += 1f;
                    }
                }
                return MRubyValue.From(array);
            }
        }
        return MRubyValue.Nil;
    });
}
