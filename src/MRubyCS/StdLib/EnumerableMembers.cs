namespace MRubyCS.StdLib;

static class EnumerableMembers
{
    [MRubyMethod(RequiredArguments = 3)]
    public static MRubyMethod InternalUpdateHash = new((state, self) =>
    {
        var hash = (int)state.GetArgumentAsIntegerAt(0);
        var index = (int)state.GetArgumentAsIntegerAt(1);
        var hv = (int)state.GetArgumentAsIntegerAt(2);
        hash ^= hv << (index % 16);
        return new MRubyValue(hash);
    });
}
