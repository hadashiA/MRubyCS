using System;
using System.Buffers;
using System.Buffers.Text;
using System.Text;
using MRubyCS.Internals;

namespace MRubyCS.StdLib;

static class StringMembers
{
    [MRubyMethod(OptionalArguments = 1)]
    public static MRubyMethod Initialize = new((state, self) =>
    {
        if (state.TryGetArgumentAt(0, out var arg))
        {
            if (arg.Object is RString other)
            {
                var str = self.As<RString>();
                other.CopyTo(str);
            }
            else
            {
                state.Raise(Names.TypeError, state.NewString($"{state.StringifyAny(arg)} cannot be converted to String"));
            }
        }
        return self;
    });

    [MRubyMethod(OptionalArguments = 1)]
    public static MRubyMethod InitializeCopy = new((state, self) =>
    {
        var str = self.As<RString>();
        var other = state.GetArgumentAsStringAt(0);
        other.CopyTo(str);
        return self;
    });

    [MRubyMethod]
    public static MRubyMethod Intern = new((state, self) =>
    {
        var str = self.As<RString>();
        return MRubyValue.From(state.Intern(str));
    });

    [MRubyMethod]
    public static MRubyMethod Replace = new((state, self) =>
    {
        var str = self.As<RString>();
        var other = state.GetArgumentAsStringAt(0);
        other.CopyTo(str);
        return self;
    });

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

    [MRubyMethod(RequiredArguments = 1, OptionalArguments = 1)]
    public static MRubyMethod OpAref = new((state, self) =>
    {
        var str = self.As<RString>();
        var index = state.GetArgumentAt(0);
        var rangeLength = -1;
        if (state.TryGetArgumentAt(1, out var arg1))
        {
            rangeLength = (int)state.ToInteger(arg1);
        }
        var result = str.GetAref(index, rangeLength);
        return result != null ? MRubyValue.From(result) : MRubyValue.Nil;
    });

    [MRubyMethod]
    public static MRubyMethod ToSym = new((state, self) =>
    {
        var str = self.As<RString>();
        var sym = state.Intern(str.AsSpan());
        return MRubyValue.From(sym);
    });

    [MRubyMethod]
    public static MRubyMethod ToS = new((state, self) =>
    {
        if (state.ClassOf(self) == state.StringClass)
        {
            return MRubyValue.From(self.As<RString>().Dup());
        }
        return self;
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

    [MRubyMethod]
    public static MRubyMethod Size = new((state, self) =>
    {
        var str = self.As<RString>();
        var charCount = Encoding.UTF8.GetCharCount(str.AsSpan());
        return MRubyValue.From(charCount);
    });

    [MRubyMethod]
    public static MRubyMethod ByteCount = new((state, self) =>
    {
        var str = self.As<RString>();
        return MRubyValue.From(str.AsSpan().Length);
    });

    [MRubyMethod]
    public static MRubyMethod Empty = new((state, self) =>
    {
        return MRubyValue.From(self.As<RString>().Length <= 0);
    });

    public static MRubyMethod Times = new((state, self) =>
    {
        var n = state.GetArgumentAsIntegerAt(0);
        if (n < 0)
        {
            state.Raise(Names.ArgumentError, "negative argument"u8);
        }

        var str = self.As<RString>();
        var newLength = str.Length * n;
        var buffer = new byte[newLength];
        var result = state.NewStringOwned(buffer);

        var src = str.AsSpan();
        var dst = buffer.AsSpan();
        for (var i = 0; i < n; i++)
        {
            src.CopyTo(dst);
            dst = dst[src.Length..];
        }
        return MRubyValue.From(result);
    });

    public static MRubyMethod Capitalize = new((state, self) =>
    {
        var str = self.As<RString>();
        var result = str.Dup();
        result.Capitalize();
        return MRubyValue.From(result);
    });

    public static MRubyMethod CapitalizeBang = new((state, self) =>
    {
        var str = self.As<RString>();
        str.Capitalize();
        return MRubyValue.Nil;
    });

    [MRubyMethod(OptionalArguments = 1)]
    public static MRubyMethod Chomp = new((state, self) =>
    {
        var str = self.As<RString>();
        var result = str.Dup();
        if (state.TryGetArgumentAt(0, out var arg0))
        {
            state.EnsureValueType(arg0, MRubyVType.String);
            var paragraph = arg0.As<RString>();
            result.Chomp(paragraph.AsSpan());
        }
        else
        {
            result.Chomp();
        }
        return MRubyValue.From(result);
    });

    [MRubyMethod(OptionalArguments = 1)]
    public static MRubyMethod ChompBang = new((state, self) =>
    {
        var str = self.As<RString>();
        if (state.TryGetArgumentAt(0, out var arg0))
        {
            state.EnsureValueType(arg0, MRubyVType.String);
            var paragraph = arg0.As<RString>();
            str.Chomp(paragraph.AsSpan());
        }
        else
        {
            str.Chomp();
        }
        return MRubyValue.Nil;
    });

    [MRubyMethod]
    public static MRubyMethod Chop = new((state, self) =>
    {
        var str = self.As<RString>();
        var result = str.Dup();
        result.Chop();
        return MRubyValue.From(result);
    });

    [MRubyMethod]
    public static MRubyMethod ChopBang = new((state, self) =>
    {
        var str = self.As<RString>();
        str.Chop();
        return MRubyValue.Nil;
    });

    [MRubyMethod]
    public static MRubyMethod Downcase = new((state, self) =>
    {
        var str = self.As<RString>();
        var result = str.Dup();
        result.Downcase();
        return MRubyValue.From(result);
    });

    [MRubyMethod]
    public static MRubyMethod DowncaseBang = new((state, self) =>
    {
        var str = self.As<RString>();
        str.Downcase();
        return MRubyValue.Nil;
    });

    [MRubyMethod]
    public static MRubyMethod Upcase = new((state, self) =>
    {
        var str = self.As<RString>();
        var result = str.Dup();
        result.Upcase();
        return MRubyValue.From(result);
    });

    [MRubyMethod]
    public static MRubyMethod UpcaseBang = new((state, self) =>
    {
        var str = self.As<RString>();
        str.Upcase();
        return MRubyValue.Nil;
    });
}
