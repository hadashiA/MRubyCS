using System;
using System.Runtime.CompilerServices;

namespace MRubyCS.Internals;

static class Utf8Helper
{
    // 先頭バイトから UTF-8 シーケンス長を判定
    public static int GetUtf8SequenceLength(byte lead)
    {
        if ((lead & 0b1000_0000) == 0b0000_0000) return 1; // 0xxxxxxx
        if ((lead & 0b1110_0000) == 0b1100_0000) return 2; // 110xxxxx
        if ((lead & 0b1111_0000) == 0b1110_0000) return 3; // 1110xxxx
        if ((lead & 0b1111_1000) == 0b1111_0000) return 4; // 11110xxx
        return 1;
    }

    public static bool IsFirstUtf8Sequence(byte lead)
    {
        return (lead & 0b1100_0000) == 0b1000_0000;
    }

    public static int FindByteIndex(ReadOnlySpan<byte> utf8, int bytesIndex)
    {
        var index = 0;
        var bytesRead = 0;
        while (index < utf8.Length && bytesRead < bytesIndex)
        {
            var seqLen = GetUtf8SequenceLength(utf8[index]);
            index += seqLen;
            bytesRead++;
        }
        return index;
    }
}

static class AsciiCode
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsDigit(byte c) => (c | 0x20) - (byte)'0' < 10;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsAlphabet(byte c) => (byte)((c | 0x20) - (byte)'a') < 26;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsAscii(byte c) => c <= 0x7f;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsUpper(byte c) => c - (byte)'A' < 26;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsLower(byte c) => c - (byte)'a' < 26;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsBlank(byte c) => c is (byte)' ' or (byte)'\t';

    public static bool IsIdentifier(byte c) => IsAlphabet(c) ||
                                               IsDigit(c) ||
                                               c == (byte)'_' ||
                                               !IsAscii(c);

    public static bool IsIdentifier(ReadOnlySpan<byte> span)
    {
        foreach (var x in span)
        {
            if (!IsIdentifier(x)) return false;
        }

        return true;
    }

    public static bool IsPrint(byte c) => (byte)(c - 0x20) < 0x5f;

    public static bool IsLineBreak(byte c) => c is (byte)'\n' or (byte)'\r';

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte ToUpper(byte b) => (byte)(b & 0xDF); // 0xDF = ~0x20

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte ToLower(byte b) => (byte)(b | 0x20);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToUpper(Span<byte> span)
    {
        for (var i = 0; i < span.Length; i++)
        {
            span[i] = ToUpper(span[i]);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToLower(Span<byte> span)
    {
        for (var i = 0; i < span.Length; i++)
        {
            span[i] = ToLower(span[i]);
        }
    }
}
