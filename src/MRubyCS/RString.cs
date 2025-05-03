using System;
using System.Runtime.CompilerServices;
using System.Text;
using MRubyCS.Internals;

namespace MRubyCS;

enum RStringRangeType
{
    /// <summary>
    /// `beg` and `len` are byte unit in `0 ... str.bytesize`
    /// </summary>
    ByteRangeCorrected = 1,

    /// <summary>
    /// `beg` and `len` are char unit in any range
    /// </summary>
    CharRange = 2,

    /// <summary>
    /// `beg` and `len` are char unit in `0 ... str.size`
    /// </summary>
    CharRangeCorrected = 3,

    /// <summary>
    /// `beg` is out of range
    /// </summary>
    OutOfRange = -1
}

public class RString : RObject, IEquatable<RString>
#if NET6_0_OR_GREATER
    , ISpanFormattable, IUtf8SpanFormattable
#endif
{
    public int Length { get; private set; }

    byte[] buffer;
    int offset;
    bool bufferOwned;

    public static RString Owned(byte[] value, RClass stringClass)
    {
        return new RString(value, 0, value.Length, stringClass);
    }

    public static RString Owned(byte[] value, int offset, int length, RClass stringClass)
    {
        return new RString(value, offset, length, stringClass);
    }

    public static RString operator+(RString a, RString b)
    {
        var buffer = new byte[a.Length + b.Length];
        a.AsSpan().CopyTo(buffer);
        b.AsSpan().CopyTo(buffer.AsSpan(a.Length));
        return Owned(buffer, a.Class);
    }

    internal RString(int capacity, RClass stringClass)
        : base(MRubyVType.String, stringClass)
    {
        buffer = new byte[capacity];
        Length = 0;
        bufferOwned = true;
    }

    internal RString(ReadOnlySpan<byte> utf8, RClass stringClass)
        : base(MRubyVType.String, stringClass)
    {
        buffer = new byte[utf8.Length];
        Length = utf8.Length;
        bufferOwned = true;
        utf8.CopyTo(buffer);
    }

    RString(RString shared) : base(MRubyVType.String, shared.Class)
    {
        buffer = shared.buffer;
        Length = shared.Length;
        offset = shared.offset;
        bufferOwned = false;
    }

    RString(byte[] buffer, int offset, int length, RClass stringClass) : base(MRubyVType.String, stringClass)
    {
        this.buffer = buffer;
        this.offset = offset;
        Length = length;
        bufferOwned = true;
        MarkAsFrozen();
    }

    public static implicit operator Span<byte>(RString str) => str.AsSpan();
    public static implicit operator ReadOnlySpan<byte>(RString str) => str.AsSpan();

    public static bool operator ==(RString? left, RString? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null)
            return false;
        return left.Equals(right);
    }

    public static bool operator !=(RString? left, RString? right)
    {
        return !(left == right);
    }

    public override string ToString()
    {
        return Encoding.UTF8.GetString(buffer, offset, Length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<byte> AsSpan() => buffer.AsSpan(offset, Length);

    public RString Dup() => new(this);

    internal override RObject Clone()
    {
        var clone = new RString(buffer.Length, Class);
        InstanceVariables.CopyTo(clone.InstanceVariables);
        return clone;
    }

    public RString SubSequence(int start, int length)
    {
        return new RString(AsSpan().Slice(start, length), Class);
    }

    public RString? SubString(int start, int length)
    {
        var charCount = Encoding.UTF8.GetCharCount(buffer, offset, Length);
        if (TryConvertRange(charCount, ref start, ref length))
        {
            var str = ToString();
            var substr = str.Substring(start, length);
            var utf8Substr = Encoding.UTF8.GetBytes(substr);
            return new RString(utf8Substr, Class);
        }
        return null;
    }

    public RString? GetAref(MRubyValue indexValue, int rangeLength = -1)
    {
        switch (CalculateStringRange(indexValue, rangeLength, out var calculatedOffset, out var calculatedLength))
        {
            case RStringRangeType.ByteRangeCorrected:
            {
                if (indexValue.Object is RString str)
                {
                    return str.Dup();
                }
                return SubSequence(calculatedOffset, calculatedLength);
            }
            case RStringRangeType.CharRange:
                return SubString(calculatedOffset, calculatedLength);

            case RStringRangeType.CharRangeCorrected:
            {
                if (indexValue.Object is RString str)
                {
                    return str.Dup();
                }
                return SubString(calculatedOffset, calculatedLength);
            }
            default:
                return null;
        }
    }

    public void Concat(RString other)
    {
        Concat(other.AsSpan());
    }

    public void Concat(ReadOnlySpan<byte> utf8)
    {
        var newLength = Length + utf8.Length;
        if (bufferOwned)
        {
            if (buffer.Length < newLength)
            {
                Array.Resize(ref buffer, newLength);
            }
        }
        else
        {
            var newBuffer = new byte[newLength];
            AsSpan().CopyTo(newBuffer);
            buffer = newBuffer;
            bufferOwned = true;
        }
        utf8.CopyTo(buffer.AsSpan(Length));
        Length = newLength;
    }

    public void Upcase()
    {
        if (Length <= 0) return;
        EnsureModifiable(Length);
        var span = AsSpan();
        AsciiCode.ToUpper(span);
    }

    public void Downcase()
    {
        if (Length <= 0) return;
        EnsureModifiable(Length);
        var span = AsSpan();
        AsciiCode.ToLower(span);
    }

    public void Capitalize()
    {
        if (Length <= 0) return;
        EnsureModifiable(Length);

        var firstChar = buffer[offset];
        if (AsciiCode.IsLower(firstChar))
        {
            buffer[offset] = AsciiCode.ToUpper(firstChar);
            AsciiCode.ToLower(buffer.AsSpan(offset + 1));
        }
        else
        {
            AsciiCode.ToLower(buffer.AsSpan());
        }
    }

    public void Chomp()
    {
        var span = AsSpan();
        if (span.Length > 0)
        {
            switch (span[^1])
            {
                case (byte)'\n':
                    if (span.Length > 1 && span[^2] == '\r')
                    {
                        Length -= 2;
                    }
                    else
                    {
                        Length--;
                    }
                    break;
                case (byte)'\r':
                    Length--;
                    break;
            }
        }
    }

    public void Chomp(ReadOnlySpan<byte> paragraph)
    {
        var span = AsSpan();
        var index = span.LastIndexOf(paragraph);
        if (index >= 0 && span.Length - index == paragraph.Length)
        {
            Length -= paragraph.Length;
        }
    }

    public void Chop()
    {
        var span = AsSpan();
        if (span.Length > 0)
        {
            var lastChar = span[^1];
            switch (lastChar)
            {
                case (byte)'\n':
                    if (span.Length > 1 && span[^2] == '\r')
                    {
                        Length -= 2;
                    }
                    else
                    {
                        Length--;
                    }
                    break;
                case var _ when AsciiCode.IsAscii(lastChar):
                    Length--;
                    break;
                default: // parse as utf8
                    var index = span.Length - 1;
                    while (index > 0 && (span[index] & 0b1100_0000) == 0b1000_0000)
                    {
                        index--;
                    }
                    var lastCharFirstByte = span[index];
                    if      ((lastCharFirstByte & 0b1000_0000) == 0b0000_0000) Length -= 1;
                    else if ((lastCharFirstByte & 0b1110_0000) == 0b1100_0000) Length -= 2;
                    else if ((lastCharFirstByte & 0b1111_0000) == 0b1110_0000) Length -= 3;
                    else if ((lastCharFirstByte & 0b1111_1000) == 0b1111_0000) Length -= 4;
                    break;
            }
        }

    }

    public void CopyTo(RString other)
    {
        other.EnsureModifiable(Length);
        other.Length = Length;
        AsSpan().CopyTo(other.AsSpan());
    }

    // TODO:
    public int CompareTo(RString other)
    {
        var a = Encoding.UTF8.GetString(AsSpan());
        var b = Encoding.UTF8.GetString(other.AsSpan());
        return string.CompareOrdinal(a, b) switch
        {
            > 0 => 1,
            < 0 => -1,
            _ => 0
        };
    }

    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        FormattableString formattable =
            $"{nameof(buffer)}: {buffer}, {nameof(Length)}: {Length}, {nameof(bufferOwned)}: {bufferOwned}";
        return formattable.ToString(formatProvider);
    }

#if NET6_0_OR_GREATER
    public bool TryFormat(
        Span<char> destination,
        out int charsWritten,
        ReadOnlySpan<char> format,
        IFormatProvider? provider)
    {
        return destination.TryWrite(provider,
            $"{nameof(buffer)}: {buffer}, {nameof(Length)}: {Length}, {nameof(bufferOwned)}: {bufferOwned}",
            out charsWritten);
    }

    public bool TryFormat(
        Span<byte> destination,
        out int bytesWritten,
        ReadOnlySpan<char> format,
        IFormatProvider? provider)
    {
        var span = AsSpan();
        if (destination.Length < span.Length)
        {
            bytesWritten = default;
            return false;
        }
        span.CopyTo(destination);
        bytesWritten = span.Length;
        return true;
    }
#endif

    public int IndexOf(ReadOnlySpan<byte> str, int offset = 0)
    {
        if (Length - offset < str.Length)
        {
            return -1;
        }
        if (offset > 0)
        {
            str = str[..offset];
        }
        return AsSpan().IndexOf(str);
    }

    RStringRangeType CalculateStringRange(
        MRubyValue index,
        int indexLength,
        out int calculatedOffset,
        out int calculatedLength)
    {
        if (indexLength >= 0)
        {
            calculatedOffset = (int)index.IntegerValue;
            calculatedLength = indexLength;
            return RStringRangeType.CharRange;
        }

        if (index.IsInteger)
        {
            calculatedOffset = (int)index.IntegerValue;
            calculatedLength = 1;
            return RStringRangeType.CharRange;
        }

        switch (index.Object)
        {
            case RString str:
                calculatedOffset = IndexOf(str);
                calculatedLength = str.Length;
                return calculatedOffset < 0
                    ? RStringRangeType.OutOfRange
                    : RStringRangeType.ByteRangeCorrected;
            case RRange range:
                if (range.Calculate(Length, true, out calculatedOffset, out calculatedLength) == RangeCalculateResult.Ok)
                {
                    return RStringRangeType.CharRangeCorrected;
                }
                break;
        }

        calculatedOffset = default;
        calculatedLength = default;
        return RStringRangeType.OutOfRange;
    }

    internal static uint GetHashCode(ReadOnlySpan<byte> span)
    {
        const uint OffsetBasis = 2166136261u;
        const uint FnvPrime = 16777619u;
        var hash = OffsetBasis;
        foreach (var b in span)
        {
            hash ^= b;
            hash *= FnvPrime;
        }
        return hash;
    }
    public override int GetHashCode()
    {
        return unchecked((int)GetHashCode(AsSpan()));
    }

    public bool Equals(RString? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return AsSpan().SequenceEqual(other.AsSpan());
    }

    public override bool Equals(object? obj)
    {
        if (obj is null) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return Equals((RString)obj);
    }

    bool TryConvertRange(int maxLength, ref int start, ref int length)
    {
        if (maxLength < start || length < 0) return false;
        if (start < 0)
        {
            start += maxLength;
            if (start < 0) return false;
        }

        if (length > maxLength - start)
        {
            length = maxLength - start;
        }

        if (length <= 0)
        {
            length = 0;
        }

        return true;
    }

    void EnsureModifiable(int capacity, bool expandLength = false)
    {
        if (buffer.Length - offset < capacity)
        {
            var newLength = buffer.Length * 2;
            if (newLength - offset < capacity)
            {
                newLength = capacity;
            }

            if (bufferOwned)
            {
                Array.Resize(ref buffer, newLength);
            }
            else
            {
                var newBuffer = new byte[newLength];
                buffer.AsSpan(offset).CopyTo(newBuffer);
                buffer = newBuffer;
                offset = 0;
                bufferOwned = true;
            }
        }
        else if (!bufferOwned)
        {
            buffer = AsSpan().ToArray();
            bufferOwned = true;
        }

        if (expandLength)
        {
            Length = capacity;
        }
    }
}
