using System;
using System.Buffers;
using System.Buffers.Text;
using MRubyCS.Internals;

namespace MRubyCS.StdLib;

static class StringMembers
{
    [MRubyMethod]
    public static MRubyMethod Inspect = new((state, self) =>
    {
        var str = self.As<RString>();
        var output = ArrayPool<byte>.Shared.Rent(str.Length * 2 + 2);

        int written;
        while (!NamingRule.TryEscape(str.AsSpan(), true, output, out written))
        {
            ArrayPool<byte>.Shared.Return(output);
            output = ArrayPool<byte>.Shared.Rent(output.Length * 2);
        }
        ArrayPool<byte>.Shared.Return(output);

        return MRubyValue.From(state.NewString(output.AsSpan(0, written)));
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod OpEq = new((state, self) =>
    {
        var other = state.GetArgumentAt(0);
        if (other.Object is RString otherString)
        {
            return MRubyValue.From(self.As<RString>().Equals(otherString));
        }
        return MRubyValue.False;
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod OpCmp = new((state, self) =>
    {
        var other = state.GetArgumentAt(0);
        if (other.Object is RString otherStr)
        {
            var str = self.As<RString>();
            return MRubyValue.From(str.CompareTo(otherStr));
        }
        return MRubyValue.Nil;
    });

    [MRubyMethod]
    public static MRubyMethod ToSym = new((state, self) =>
    {
        var str = self.As<RString>();
        var sym = state.Intern(str.AsSpan());
        return MRubyValue.From(sym);
    });

    [MRubyMethod(OptionalArguments = 1)]
    public static MRubyMethod ToI = new((state, self) =>
    {
        var str = self.As<RString>();

        var format = 'g';
        if (state.TryGetArgumentAt(0, out var arg0))
        {
            var basis = state.ToInteger(arg0);
            switch (basis)
            {
                case 2:
                    format = 'b';
                    break;
                case 8:
                    format = 'o';
                    break;
                case 16:
                    format = 'x';
                    break;
                case 10:
                    format = 'g';
                    break;
                default:
                    state.Raise(Names.ArgumentError, state.NewString($"invalid radix {basis}"));
                    format = default;
                    break;
            }
        }

        Utf8Parser.TryParse(str.AsSpan(), out int result, out var consumed, format);
        return MRubyValue.From(result);
    });
}