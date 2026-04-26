using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace MRubyCS;

public readonly record struct Symbol(uint Value)
{
    public static readonly Symbol Empty = new(0);
    public readonly uint Value = Value;
}

class SymbolTable
{
    readonly record struct Key(int HashCode)
    {
        const uint OffsetBasis = 2166136261u;
        const uint FnvPrime = 16777619u;

        public static Key Create(ReadOnlySpan<byte> symbolName)
        {
            var hash = OffsetBasis;
            foreach (var b in symbolName)
            {
                hash ^= b;
                hash *= FnvPrime;
            }
            return new Key(unchecked((int)hash));
        }

        public override int GetHashCode() => HashCode;
    }

    const int PackLengthMax = 5;

    static readonly byte[] PackTable = "_abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789"u8.ToArray();

    // Reverse lookup: byte -> (PackTable index + 1). Zero means not packable.
    static readonly byte[] UnpackTable = BuildUnpackTable();

    static byte[] BuildUnpackTable()
    {
        var table = new byte[256];
        for (var i = 0; i < PackTable.Length; i++)
        {
            table[PackTable[i]] = (byte)(i + 1);
        }
        return table;
    }

    [ThreadStatic]
    static byte[]? nameBuffer;

    static uint lastId = (uint)Names.Count;

    static byte[] ThreadStaticBuffer() => nameBuffer ??= new byte[32];

    readonly Dictionary<Symbol, byte[]> names = new(64);
    readonly Dictionary<Key, Symbol> symbols = new(64);

    public Symbol Intern(ReadOnlySpan<byte> utf8)
    {
        if (TryFind(utf8, out var symbol))
        {
            return symbol;
        }

        symbol = new Symbol(++lastId);
        var nameBuf = new byte[utf8.Length];
        utf8.CopyTo(nameBuf);
        names.Add(symbol, nameBuf);
        symbols.Add(Key.Create(utf8), symbol);
        return symbol;
    }

    public Symbol InternLiteral(byte[] utf8)
    {
        if (TryFind(utf8, out var symbol))
        {
            return symbol;
        }
        symbol = new Symbol(++lastId);
        names.Add(symbol, utf8);
        symbols.Add(Key.Create(utf8), symbol);
        return symbol;
    }

    public Symbol Intern(string s)
    {
        var buf = ThreadStaticBuffer();
        var maxLength = Encoding.UTF8.GetMaxByteCount(s.Length);
        if (buf.Length < maxLength)
        {
            buf = nameBuffer = new byte[maxLength];
        }
        var bytesWritten = Encoding.UTF8.GetBytes(s, 0, s.Length, buf, 0);
        return Intern(buf.AsSpan(0, bytesWritten));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryFind(ReadOnlySpan<byte> utf8, out Symbol symbol)
    {
        // Try inline-pack first. Packable presyms have IDs equal to their packed
        // value (assigned by the source generator), so they resolve here without
        // touching the hash dispatch.
        if (TryInlinePack(utf8, out symbol))
        {
            return true;
        }
        var key = Key.Create(utf8);
        // Check dynamic dict first; for runtime-heavy workloads dynamic symbols
        // dominate, while non-packable presyms (mostly long class/error names) are
        // rare in hot lookup paths.
        return symbols.TryGetValue(key, out symbol)
               || Names.TryFind(key.HashCode, utf8, out symbol);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<byte> NameOf(Symbol symbol)
    {
        if (symbol.Value == 0)
        {
            return default;
        }
        if (Names.TryGetName(symbol, out var c))
        {
            return c;
        }
        if (IsInlined(symbol))
        {
            return InlineUnpackCached(symbol);
        }
        return names[symbol];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static bool IsInlined(Symbol symbol) => symbol.Value >= 1u << 24;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static bool TryInlinePack(ReadOnlySpan<byte> utf8, out Symbol packedSymbol)
    {
        if ((uint)utf8.Length - 1 >= PackLengthMax)
        {
            // length == 0 or length > PackLengthMax
            packedSymbol = default;
            return false;
        }

        uint packedValue = 0;
        var table = UnpackTable;
        for (var i = 0; i < utf8.Length; i++)
        {
            var bits = (uint)table[utf8[i]];
            if (bits == 0)
            {
                packedSymbol = default;
                return false;
            }
            packedValue |= bits << (24 - i * 6);
        }

        packedSymbol = new Symbol(packedValue);
        return true;
    }

    ReadOnlySpan<byte> InlineUnpackCached(Symbol symbol)
    {
        if (names.TryGetValue(symbol, out var cached))
        {
            return cached;
        }

        Span<byte> buf = stackalloc byte[PackLengthMax];
        int i;
        for (i = 0; i < PackLengthMax; i++)
        {
            var bits = symbol.Value >> (24 - i * 6) & 0x3f;
            if (bits == 0) break;
            buf[i] = PackTable[(int)bits - 1];
        }

        var name = buf[..i].ToArray();
        names[symbol] = name;
        return name;
    }
}
