namespace MRubyCS.StdLib;

static class BasicObjectMembers
{
    [MRubyMethod]
    public static MRubyMethod Not = new((_, self) => new MRubyValue(!self.Truthy));

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod OpEq = new((state, self) =>
    {
        return self == state.GetArgumentAt(0);
    });

    public static MRubyMethod Id = new((state, self) =>
    {
        return self.ObjectId;
    });

    public static MRubyMethod Send = new((state, self) =>
    {
        return state.SendMeta(self);
    });

    public static MRubyMethod InstanceEval = new((state, self) =>
    {
        var block = state.GetBlockArgument(false);
        return state.EvalUnder(self, block!, state.SingletonClassOf(self));
    });

    public static MRubyMethod MethodMissing = new((state, self) =>
    {
        var methodId = state.GetArgumentAsSymbolAt(0);
        var args = state.GetRestArgumentsAfter(1);
        var array = state.NewArray(args);
        state.RaiseMethodMissing(methodId, self, array);
        return MRubyValue.Nil;
    });
}
