namespace MRubyCS.StdLib;

static class RangeMembers
{
    [MRubyMethod(RequiredArguments = 2, OptionalArguments = 1)]
    public static MRubyMethod Initialize = new((state, self) =>
    {
        var range = self.As<RRange>();
        if (range.IsFrozen)
        {
            state.Raise(Names.NameError, "'initialize' called twice"u8);
        }
        range.Begin = state.GetArgumentAt(0);
        range.End = state.GetArgumentAt(1);
        if (state.TryGetArgumentAt(2, out var exclusiveValue))
        {
            range.Exclusive = exclusiveValue.Truthy;
        }
        range.MarkAsFrozen();
        return self;
    });

    public static MRubyMethod InitializeCopy = new((state, self) =>
    {
        var range = self.As<RRange>();
        if (range.IsFrozen)
        {
            state.Raise(Names.NameError, "'initialize_copy' called twice"u8);
        }
        var src = state.GetArgumentAsRangeAt(0);
        if (range == src)
        {
            return self;
        }

        range.Begin = src.Begin;
        range.End = src.End;
        range.Exclusive = src.Exclusive;
        range.MarkAsFrozen();
        return self;
    });

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
        return self.As<RRange>().Exclusive;
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
        return state.ValueEquals(range.Begin, rangeOther.Begin) &&
            state.ValueEquals(range.End, rangeOther.End) &&
            range.Exclusive == rangeOther.Exclusive;
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
            return result;
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
            return result;
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
            result.Concat(state.Inspect(range.Begin));
        }
        result.Concat(range.Exclusive ? "..."u8 : ".."u8);
        if (!range.End.IsNil)
        {
            result.Concat(state.Inspect(range.End));
        }
        return MRubyValue.From(result);
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod OpEql = new((state, self) =>
    {
        var arg0 = state.GetArgumentAt(0);
        if (self == arg0) return MRubyValue.True;

        var range = self.As<RRange>();
        if (arg0.Object is not RRange rangeOther)
        {
            return MRubyValue.False;
        }

        // Use eql? instead of == for stricter equality
        var beginEql = state.Send(range.Begin, Names.QEql, rangeOther.Begin);
        var endEql = state.Send(range.End, Names.QEql, rangeOther.End);
        return beginEql.Truthy && endEql.Truthy && range.Exclusive == rangeOther.Exclusive;
    });

    public static MRubyMethod First = new((state, self) =>
    {
        return self.As<RRange>().Begin;
    });

    public static MRubyMethod Last = new((state, self) =>
    {
        return self.As<RRange>().End;
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
                    array[i] = a + i;
                }

                return array;
            }

            if (range.End.IsFloat)
            {
                var a = (float)range.Begin.IntegerValue;
                var b = range.End.FloatValue;
                if (a > b)
                {
                    return state.NewArray(0);
                }

                var array = state.NewArray((int)(b - a) + 1);
                var i = 0;
                if (range.Exclusive)
                {
                    while (a < b)
                    {
                        array[i++] = (int)a;
                        a += 1f;
                    }
                }
                else
                {
                    while (a <= b)
                    {
                        array[i++] = (int)a;
                        a += 1f;
                    }
                }
                return array;
            }
        }
        return MRubyValue.Nil;
    });
}
