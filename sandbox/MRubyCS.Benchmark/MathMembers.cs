using System;

namespace MRubyCS.Benchmark;

class MathMembers
{
    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod Sin = new((state, self) =>
    {
        var arg = state.GetArgumentAt(0);
        var b = arg.VType switch
        {
            MRubyVType.Float => arg.FloatValue,
            _ => state.ToFloat(arg)
        };
        return MRubyValue.From(Math.Sin(b));
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod Cos = new((state, self) =>
    {
        var arg = state.GetArgumentAt(0);
        var b = arg.VType switch
        {
            MRubyVType.Float => arg.FloatValue,
            _ => state.ToFloat(arg)
        };
        return MRubyValue.From(Math.Cos(b));
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod Sqrt = new((state, self) =>
    {
        var arg = state.GetArgumentAt(0);
        var b = arg.VType switch
        {
            MRubyVType.Float => arg.FloatValue,
            _ => state.ToFloat(arg)
        };
        return MRubyValue.From(Math.Sqrt(b));
    });
}