namespace MRubyCS.StdLib;

static class NumericMembers
{
    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod Eql = new((state, self) =>
    {
        var other = state.GetArgumentAt(0);
        if (self.IsFloat)
        {
            if (!other.IsFloat) return MRubyValue.False;
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            return self.FloatValue == other.FloatValue;
        }

        if (self.IsInteger)
        {
            if (!other.IsInteger) return MRubyValue.False;
            return self.IntegerValue == other.IntegerValue;
        }

        return self == other;
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

        return x < y;
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

        return x <= y;
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

        return x > y;
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

        return x >= y;
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod OpCmp = new((state, self) =>
    {
        var other = state.GetArgumentAt(0);
        if (self.IsInteger)
        {
            if (other.IsInteger)
            {
                return self.IntegerValue.CompareTo(other.IntegerValue);
            }
            if (other.IsFloat)
            {
                return ((double)self.IntegerValue).CompareTo(other.FloatValue);
            }
        }
        else if (self.IsFloat)
        {
            if (other.IsInteger)
            {
                return self.FloatValue.CompareTo((double)other.IntegerValue);
            }
            if (other.IsFloat)
            {
                return self.FloatValue.CompareTo(other.FloatValue);
            }
        }
        return -2;
    });
}