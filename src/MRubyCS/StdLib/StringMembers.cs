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

        var indexValue = state.GetArgumentAt(0);
        var rangeLength = default(int?);
        if (state.TryGetArgumentAt(1, out var arg1))
        {
            rangeLength = (int)state.ToInteger(arg1);
        }

        var result = str.GetPartial(state, indexValue, rangeLength);
        return result != null ? MRubyValue.From(result) : MRubyValue.Nil;
    });

    [MRubyMethod(RequiredArguments = 2, OptionalArguments = 1)]
    public static MRubyMethod OpAset = new((state, self) =>
    {
        MRubyValue index;
        RString? value;
        int? rangeLength = null;
        var argc = state.GetArgumentCount();
        switch (argc)
        {
            case 2:
                index = state.GetArgumentAt(0);
                value = state.GetArgumentAsStringAt(1);
                break;
            case 3:
                index = state.GetArgumentAt(0);
                rangeLength = (int)state.GetArgumentAsIntegerAt(1);
                value = state.GetArgumentAsStringAt(2);
                break;
            default:
                state.RaiseArgumentNumberError(argc, 2, 3);
                return MRubyValue.Nil;
        }

        var str = self.As<RString>();
        str.SetPartial(state, index, rangeLength, value);
        return self;
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
        var source = self.As<RString>().AsSpan();

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

        bool result;
        long value;
        if (source.Length > 64)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(source.Length);
            AsciiCode.PrepareNumber(source, buffer);
            result = format == 'b'
                ? AsciiCode.TryParseBinary(buffer, out value)
                : Utf8Parser.TryParse(buffer, out value, out var consumed, format);
            ArrayPool<byte>.Shared.Return(buffer);
        }
        else
        {
            Span<byte> buffer = stackalloc byte[source.Length];
            AsciiCode.PrepareNumber(source, buffer);
            result = format == 'b'
            ? AsciiCode.TryParseBinary(buffer, out value)
            : Utf8Parser.TryParse(buffer, out value, out var consumed, format);
        }
        return result ? MRubyValue.From(value) : MRubyValue.From(0);
    });

    [MRubyMethod]
    public static MRubyMethod ToF = new((state, self) =>
    {
        var source = self.As<RString>().AsSpan();

        bool result;
        double value;
        if (source.Length > 64)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(source.Length);
            AsciiCode.PrepareNumber(source, buffer);
            ArrayPool<byte>.Shared.Return(buffer);
            result = Utf8Parser.TryParse(buffer, out value, out var consumed, 'g');
        }
        else
        {
            Span<byte> buffer = stackalloc byte[source.Length];
            AsciiCode.PrepareNumber(source, buffer);
            result = Utf8Parser.TryParse(buffer, out value, out var consumed, 'g');
        }
        return result ? MRubyValue.From(value) : MRubyValue.From(0f);
    });

    [MRubyMethod]
    public static MRubyMethod Size = new((state, self) =>
    {
        var str = self.As<RString>();
        var charCount = Encoding.UTF8.GetCharCount(str.AsSpan());
        return MRubyValue.From(charCount);
    });

    [MRubyMethod]
    public static MRubyMethod Empty = new((state, self) =>
    {
        return MRubyValue.From(self.As<RString>().Length <= 0);
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod Include = new((state, self) =>
    {
        var str = self.As<RString>();
        var v = state.GetArgumentAsStringAt(0);
        var i = str.AsSpan().IndexOf(v.AsSpan());
        return MRubyValue.From(i >= 0);
    });

    [MRubyMethod(RequiredArguments = 1, OptionalArguments = 1)]
    public static MRubyMethod Index = new((state, self) =>
    {
        var str = self.As<RString>();
        var argc = state.GetArgumentCount();

        RString target = default!;
        var pos = 0;
        switch (argc)
        {
            case 1:
                target = state.GetArgumentAsStringAt(0);
                break;
            case 2:
                target = state.GetArgumentAsStringAt(0);
                pos = (int)state.GetArgumentAsIntegerAt(1);
                break;
            default:
                state.RaiseArgumentNumberError(argc, 1, 2);
                break;
        }
        var result = str.IndexOf(target, pos);
        return result < 0 ? MRubyValue.Nil : MRubyValue.From(result);
    });

    [MRubyMethod(RequiredArguments = 1, OptionalArguments = 1)]
    public static MRubyMethod RIndex = new((state, self) =>
    {
        var str = self.As<RString>();
        var argc = state.GetArgumentCount();

        RString target = default!;
        var pos = str.Length;
        switch (argc)
        {
            case 1:
                target = state.GetArgumentAsStringAt(0);
                break;
            case 2:
                target = state.GetArgumentAsStringAt(0);
                pos = (int)state.GetArgumentAsIntegerAt(1);
                break;
            default:
                state.RaiseArgumentNumberError(argc, 1, 2);
                break;
        }
        var result = str.IndexOfFromRight(target, pos);
        return result < 0 ? MRubyValue.Nil : MRubyValue.From(result);
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
        state.EnsureNotFrozen(str);

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
        state.EnsureNotFrozen(str);

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
        state.EnsureNotFrozen(str);
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
        state.EnsureNotFrozen(str);
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
        state.EnsureNotFrozen(str);
        str.Upcase();
        return MRubyValue.Nil;
    });

    [MRubyMethod]
    public static MRubyMethod Reverse = new((state, self) =>
    {
        var str = self.As<RString>();
        var buf = Utf8Helper.Reverse(str.AsSpan());
        return MRubyValue.From(state.NewStringOwned(buf));
    });

    [MRubyMethod]
    public static MRubyMethod ReverseBang = new((state, self) =>
    {
        var str = self.As<RString>();
        state.EnsureNotFrozen(str);

        var buf = Utf8Helper.Reverse(str.AsSpan());
        str.MakeModifiable(str.Length);
        buf.CopyTo(str.AsSpan());
        return self;
    });

    [MRubyMethod(OptionalArguments = 2)]
    public static MRubyMethod Split = new((state, self) =>
    {
        var str = self.As<RString>();
        var argc = state.GetArgumentCount();

        var splitType = RStringSplitType.String;
        var separator = default(RString?);
        var limit = -1;

        switch (argc)
        {
            case 0:
                splitType = RStringSplitType.Whitespaces;
                break;
            case 1:
            {
                var arg0 = state.GetArgumentAt(0);
                if (!arg0.IsNil)
                {
                    state.EnsureValueType(arg0, MRubyVType.String);
                    separator = arg0.As<RString>();
                }
                break;
            }
            case 2:
            {
                var arg0 = state.GetArgumentAt(0);
                if (!arg0.IsNil)
                {
                    state.EnsureValueType(arg0, MRubyVType.String);
                    separator = arg0.As<RString>();
                }
                limit = (int)state.GetArgumentAsIntegerAt(1);
                break;
            }
            default:
                state.RaiseArgumentNumberError(argc, 0, 2);
                break;
        }

        if (separator == null || separator.Length == 1 && separator.AsSpan()[0] == (byte)' ')
        {
            splitType = RStringSplitType.Whitespaces;
        }

        var result = state.NewArray();
        switch (splitType)
        {
            case RStringSplitType.Whitespaces:
            {
                str.SplitByWhitespacesTo(result, limit);
                break;
            }
            case RStringSplitType.String:
            {
                str.SplitBytSeparatorTo(result, separator!, limit);
                break;
            }
        }
        return MRubyValue.From(result);
    });

    [MRubyMethod]
    public static MRubyMethod ByteCount = new((state, self) =>
    {
        var str = self.As<RString>();
        return MRubyValue.From(str.AsSpan().Length);
    });

    [MRubyMethod]
    public static MRubyMethod Bytes = new((state, self) =>
    {
        var span = self.As<RString>().AsSpan();
        var array = state.NewArray(span.Length);
        foreach (var x in span)
        {
            array.Push(MRubyValue.From(x));
        }
        return MRubyValue.From(array);
    });

    [MRubyMethod(RequiredArguments = 1, OptionalArguments = 1)]
    public static MRubyMethod ByteIndex = new((state, self) =>
    {
        var str = self.As<RString>();

        var target = state.GetArgumentAsStringAt(0);
        var pos = 0;
        if (state.TryGetArgumentAt(1, out var arg1))
        {
            pos = (int)state.ToInteger(arg1);
        }

        var index = str.ByteIndexOf(target, pos);
        return index < 0 ? MRubyValue.Nil : MRubyValue.From(index);
    });

    [MRubyMethod(RequiredArguments = 1, OptionalArguments = 1)]
    public static MRubyMethod BytesSlice = new((state, self) =>
    {
        int start;
        int length;
        var empty = true;

        var str = self.As<RString>();

        var argc = state.GetArgumentCount();
        switch (argc)
        {
            case 1:
                var arg0 = state.GetArgumentAt(0);
                if (arg0.Object is RRange range)
                {
                    var rangeResult = range.Calculate(str.Length, true, out start, out length);
                    if (rangeResult != RangeCalculateResult.Ok)
                    {
                        return MRubyValue.Nil;
                    }
                }
                else
                {
                    start = (int)state.ToInteger(arg0);
                    length = 1;
                    empty = false;
                }
                break;
            case 2:
                start = (int)state.GetArgumentAsIntegerAt(0);
                length = (int)state.GetArgumentAsIntegerAt(1);
                break;
            default:
                state.RaiseArgumentNumberError(argc, 1, 2);
                return MRubyValue.Nil;
        }

        if (empty || length != 0)
        {
            var result = str.SubByteSequence(start, length);
            return result != null ? MRubyValue.From(result) : MRubyValue.Nil;
        }

        return MRubyValue.Nil;
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod GetByte = new((state, self) =>
    {
        var str = self.As<RString>();
        var pos = (int)state.GetArgumentAsIntegerAt(0);
        if (pos < 0)
        {
            pos += str.Length;
        }
        if (pos < 0 || str.Length <= pos)
        {
            return MRubyValue.Nil;
        }

        return MRubyValue.From(str.AsSpan()[pos]);
    });

    [MRubyMethod(RequiredArguments = 2)]
    public static MRubyMethod SetByte = new((state, self) =>
    {
        var str = self.As<RString>();

        var pos = (int)state.GetArgumentAsIntegerAt(0);
        var value = (int)state.GetArgumentAsIntegerAt(1);
        if (pos < -str.Length || str.Length <= pos)
        {
            state.Raise(Names.IndexError, state.NewString($"index {pos} out of string"));
        }
        if (pos < 0)
        {
            pos += str.Length;
        }
        str.AsSpan()[pos] = (byte)(value & 0xff);
        return self;
    });

    [MRubyMethod(RequiredArguments = 3)]
    public static MRubyMethod InternalSubReplace = new((state, self) =>
    {
        var str = self.As<RString>();
        var pattern = state.GetArgumentAsStringAt(0);
        var match = state.GetArgumentAsStringAt(1);
        var found = state.GetArgumentAsIntegerAt(2);

        var p = pattern.AsSpan();
        var m = pattern.AsSpan();

        var result = state.NewString(0);
        for (var i = 0; i < pattern.Length; i++)
        {
            if (p[i] != '\\' || i + 1 >= pattern.Length)
            {
                result.Concat(p[i]);
                continue;
            }

            // escaped
            i++;

            switch (p[i])
            {
                case (byte)'\\':
                    result.Concat((byte)'\\');
                    break;
                case (byte)'`':
                    result.Concat(str.AsSpan(0, (int)found));
                    break;
                case (byte)'&':
                case (byte)'0':
                    result.Concat(match);
                    break;
                case (byte)'\'':
                    var pos = (int)found + match.Length;
                    if (str.Length > pos)
                    {
                        result.Concat(str.AsSpan(pos));
                    }
                    break;
                case >= (byte)'1' and <= (byte)'9':
                    // ignore sub-group match (no Regexp supported)
                    break;
                default:
                    result.Concat(p.Slice(i - 1, 2));
                    break;
            }
        }
        return MRubyValue.From(result);
    });
}
