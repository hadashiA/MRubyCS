namespace MRubyCS.StdLib;

static class NumericMembers
{
    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod Eql = new(((state, self) =>
    {
        var other = state.GetArgumentAt(0);
        if (self.IsFloat)
        {
            if (!other.IsFloat) return MRubyValue.False;
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            return MRubyValue.From(self.FloatValue == other.FloatValue);
        }

        if (self.IsInteger)
        {
            if (!other.IsInteger) return MRubyValue.False;
            return MRubyValue.From(self.IntegerValue == other.IntegerValue);
        }

        return MRubyValue.From(self == other);
    }));

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod OpCmp = new(((state, self) =>
    {
        var other = state.GetArgumentAt(0);
        if (self.IsInteger)
        {
            if (other.IsInteger)
            {
                return MRubyValue.From(self.IntegerValue.CompareTo(other.IntegerValue));
            }
            if (other.IsFloat)
            {
                return MRubyValue.From(((double)self.IntegerValue).CompareTo(other.FloatValue));
            }
        }
        else if (self.IsFloat)
        {
            if (other.IsInteger)
            {
                return MRubyValue.From(self.FloatValue.CompareTo((double)other.IntegerValue));
            }
            if (other.IsFloat)
            {
                return MRubyValue.From(self.FloatValue.CompareTo(other.FloatValue));
            }
        }
        return MRubyValue.From(-2);
    }));
}