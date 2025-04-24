namespace MRubyCS.StdLib;

static class EnumerableMembers
{
    [MRubyMethod(RequiredArguments = 3)]
    public static MRubyMethod InternalUpdateHash = new((state, self) =>
    {
        var hash = (int)state.GetArgAsInteger(0);
        var index = (int)state.GetArgAsInteger(1);
        var hv = (int)state.GetArgAsInteger(2);
        hash ^= hv << (index % 16);
        return MRubyValue.From(hash);
    });
}
