using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace MRubyCS;

public sealed class RHash : RObject, IEnumerable<KeyValuePair<MRubyValue, MRubyValue>>
{
    /// <summary>Pluggable backing storage. Replaceable on demote so the
    /// .NET object identity of <see cref="RHash"/> stays stable across
    /// type-mismatched mutations (Ruby in-place semantics).</summary>
    internal RHashBackend backend;

    readonly IEqualityComparer<MRubyValue> keyComparer;
    readonly IEqualityComparer<MRubyValue> valueComparer;
    readonly int initialCapacity;

    public int Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => backend.Length;
    }

    public ReadOnlySpan<MRubyValue> Keys
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => backend.Keys;
    }

    public ReadOnlySpan<MRubyValue> Values
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => backend.Values;
    }

    public MRubyValue? DefaultValue { get; set; }
    public RProc? DefaultProc { get; set; }

    internal RHash(
        int capacity,
        IEqualityComparer<MRubyValue> keyComparer,
        IEqualityComparer<MRubyValue> valueComparer,
        RClass hashClass) : base(MRubyVType.Hash, hashClass)
    {
        this.keyComparer = keyComparer;
        this.valueComparer = valueComparer;
        initialCapacity = capacity;
        // Default to symbol-keyed: in Ruby workloads (kwargs, attribute lookup,
        // option bags) symbol keys dominate. The first non-symbol Add demotes
        // to a generic backend.
        backend = new RHashSymbolKeyedBackend(capacity);
    }

    RHash(RHash source) : base(MRubyVType.Hash, source.Class)
    {
        keyComparer = source.keyComparer;
        valueComparer = source.valueComparer;
        initialCapacity = source.initialCapacity;
        // Shallow copy of contents — match Ruby Hash#dup which copies entries
        // but does not share underlying storage.
        backend = source.backend switch
        {
            RHashSymbolKeyedBackend sym => CloneSymbol(sym),
            RHashGenericBackend gen => CloneGeneric(gen),
            _ => throw new InvalidOperationException(),
        };
    }

    static RHashSymbolKeyedBackend CloneSymbol(RHashSymbolKeyedBackend src)
    {
        var dst = new RHashSymbolKeyedBackend(src.Length);
        for (var i = 0; i < src.Length; i++)
        {
            src.GetPairAt(i, out var k, out var v);
            dst.TrySet(k, v);
        }
        return dst;
    }

    RHashGenericBackend CloneGeneric(RHashGenericBackend src)
    {
        var dst = new RHashGenericBackend(src.Length, keyComparer, valueComparer);
        for (var i = 0; i < src.Length; i++)
        {
            src.GetPairAt(i, out var k, out var v);
            dst.TrySet(k, v);
        }
        return dst;
    }

    public MRubyValue this[MRubyValue key]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => backend.Get(key);
        set
        {
            if (!backend.TrySet(key, value))
            {
                backend = DemoteBackend();
                backend.TrySet(key, value);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    RHashGenericBackend DemoteBackend()
    {
        if (backend is RHashGenericBackend g) return g;
        var sym = (RHashSymbolKeyedBackend)backend;
        return sym.DemoteWithComparers(keyComparer, valueComparer);
    }

    public MRubyValue GetValueOrDefault(MRubyValue key, MRubyState state)
    {
        if (backend.TryGetValue(key, out var v)) return v;
        if (DefaultProc is { } proc)
        {
            return state.Send(proc, Names.Call, this, key);
        }
        return DefaultValue ?? MRubyValue.Nil;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(MRubyValue key, MRubyValue value)
    {
        if (!backend.TryAdd(key, value))
        {
            backend = DemoteBackend();
            backend.TryAdd(key, value);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ContainsKey(MRubyValue key) => backend.ContainsKey(key);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ContainsValue(MRubyValue value)
    {
        foreach (var t in backend.Values)
        {
            if (valueComparer.Equals(value, t)) return true;
        }
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetValue(MRubyValue key, out MRubyValue value) =>
        backend.TryGetValue(key, out value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryDelete(MRubyValue key, out MRubyValue value) =>
        backend.TryDelete(key, out value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Clear() => backend.Clear();

    public void ReplaceTo(RHash other)
    {
        // Mirror self's backend type into other.
        if (backend is RHashSymbolKeyedBackend sym)
        {
            other.backend = CloneSymbol(sym);
        }
        else
        {
            var srcGen = (RHashGenericBackend)backend;
            var dst = new RHashGenericBackend(srcGen.Length, other.keyComparer, other.valueComparer);
            for (var i = 0; i < srcGen.Length; i++)
            {
                srcGen.GetPairAt(i, out var k, out var v);
                dst.TrySet(k, v);
            }
            other.backend = dst;
        }
        other.DefaultValue = DefaultValue;
        other.DefaultProc = DefaultProc;
    }

    public bool TryShift(out MRubyValue headKey, out MRubyValue headValue) =>
        backend.TryShift(out headKey, out headValue);

    public RHash Dup() => new(this);

    internal override RObject Clone()
    {
        var clone = new RHash(initialCapacity, keyComparer, valueComparer, Class);
        InstanceVariables.CopyTo(clone.InstanceVariables);
        return clone;
    }

    public void Merge(RHash other)
    {
        if (this == other) return;
        if (other.Length == 0) return;

        for (var i = 0; i < other.backend.Length; i++)
        {
            other.backend.GetPairAt(i, out var k, out var v);
            this[k] = v;
        }
    }

    public void Rehash() => backend.Rehash();

    public struct Enumerator(RHash source) : IEnumerator<KeyValuePair<MRubyValue, MRubyValue>>
    {
        public KeyValuePair<MRubyValue, MRubyValue> Current { get; private set; }
        object IEnumerator.Current => Current;

        int index = -1;

        public bool MoveNext()
        {
            index++;
            if (index < source.backend.Length)
            {
                source.backend.GetPairAt(index, out var k, out var v);
                Current = new KeyValuePair<MRubyValue, MRubyValue>(k, v);
                return true;
            }
            return false;
        }

        public void Reset()
        {
            index = -1;
            Current = default;
        }

        public void Dispose()
        {
            index = -2;
            Current = default;
        }
    }

    public Enumerator GetEnumerator() => new(this);

    IEnumerator<KeyValuePair<MRubyValue, MRubyValue>> IEnumerable<KeyValuePair<MRubyValue, MRubyValue>>.GetEnumerator() =>
        GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
