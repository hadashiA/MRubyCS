namespace MRubyCS.StdLib;

static class ExceptionMembers
{
    [MRubyMethod(RestArguments = true, BlockArgument = true)]
    public static MRubyMethod New = new((state, self) =>
    {
        var args = state.GetRestArgumentsAfter(0);
        var block = state.GetBlockArgument();

        var c = self.As<RClass>();
        var o = new RException(null, c);
        var value = new MRubyValue(o);
        if (state.TryFindMethod(c, Names.Initialize, out var method, out _) &&
            method != MRubyMethod.Nop)
        {
            state.Send(value, Names.Initialize, args, kargs: null, block: block);
        }
        return value;
    });

    [MRubyMethod(OptionalArguments =  1)]
    public static MRubyMethod Exception = new((state, self) =>
    {
        if (!state.TryGetArgumentAt(0, out var arg) || arg == self)
        {
            return self;
        }

        var ex = state.CloneObject(self);
        ex.As<RException>().Message = state.Stringify(arg);
        return ex;
    });

    [MRubyMethod(OptionalArguments =  1)]
    public static MRubyMethod Initialize = new((state, self) =>
    {
        if (state.TryGetArgumentAt(0, out var arg))
        {
            self.As<RException>().Message = state.Stringify(arg);
        }
        return self;
    });

    [MRubyMethod]
    public static MRubyMethod ToS = new((state, self) =>
    {
        if (self.As<RException>().Message is { } message)
        {
            return message;
        }
        return state.NameOf(state.ClassOf(self));
    });

    [MRubyMethod]
    public static MRubyMethod Inspect = new((state, self) =>
    {
        var className = state.NameOf(state.ClassOf(self));
        var message = self.As<RException>().Message;
        if (message is { Length: > 0 })
        {
            return state.NewString($"{message} ({className})");
        }
        return className;
    });

    [MRubyMethod]
    public static MRubyMethod Backtrace = new((state, self) =>
    {
        var backtrace = self.As<RException>().Backtrace;
        if (backtrace is null)
        {
            return MRubyValue.Nil;
        }
        return backtrace.ToRArray(state);
    });
}
