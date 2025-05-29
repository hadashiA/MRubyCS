using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MRubyCS;

public sealed class RHash : RObject, IEnumerable<KeyValuePair<MRubyValue, MRubyValue>>
{
    public int Length => keys.Count;
    public ReadOnlySpan<MRubyValue> Keys => CollectionsMarshal.AsSpan(keys);
    public ReadOnlySpan<MRubyValue> Values => CollectionsMarshal.AsSpan(values);

    public MRubyValue? DefaultValue { get; set; }
    public RProc? DefaultProc { get; set; }

    readonly List<MRubyValue> keys;
    readonly List<MRubyValue> values;

    readonly IEqualityComparer<MRubyValue> keyComparer;
    readonly IEqualityComparer<MRubyValue> valueComparer;

    internal RHash(
        int capacity,
        IEqualityComparer<MRubyValue> keyComparer,
        IEqualityComparer<MRubyValue> valueComparer,
        RClass hashClass) : base(MRubyVType.Hash, hashClass)
    {
        this.keyComparer = keyComparer;
        this.valueComparer = valueComparer;
        keys = new List<MRubyValue>(capacity);
        values = new List<MRubyValue>(capacity);
    }

    RHash(
        List<MRubyValue> keys,
        List<MRubyValue> values,
        IEqualityComparer<MRubyValue> keyComparer,
        IEqualityComparer<MRubyValue> valueComparer,
        RClass hashClass) : base(MRubyVType.Hash, hashClass)
    {
        this.keys = keys;
        this.values = values;
        this.keyComparer = keyComparer;
        this.valueComparer = valueComparer;
    }

    public MRubyValue this[MRubyValue key]
    {
        get
        {
            for (var i = 0; i < keys.Count; i++)
            {
                if (KeyEquals(key, keys[i]))
                {
                    return values[i];
                }
            }
            return MRubyValue.Nil;
        }
        set
        {
            for (var i = 0; i < keys.Count; i++)
            {
                if (KeyEquals(key, keys[i]))
                {
                    values[i] = value;
                    return;
                }
            }
            keys.Add(key);
            values.Add(value);
        }
    }

    public MRubyValue GetValueOrDefault(MRubyValue key, MRubyState state)
    {
        for (var i = 0; i < keys.Count; i++)
        {
            if (KeyEquals(key, keys[i]))
            {
                return values[i];
            }
        }
        if (DefaultProc is { } proc)
        {
            return state.Send(MRubyValue.From(proc), Names.Call, MRubyValue.From(this), key);
        }
        return DefaultValue ?? MRubyValue.Nil;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(MRubyValue key, MRubyValue value)
    {
        if (ContainsKey(key))
        {
            throw new InvalidOperationException("Duplicate key");
        }
        keys.Add(key);
        values.Add(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ContainsKey(MRubyValue key)
    {
        foreach (var t in Keys)
        {
            if (KeyEquals(key, t)) return true;
        }
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ContainsValue(MRubyValue value)
    {
        foreach (var t in Values)
        {
            if (valueComparer.Equals(value, t)) return true;
        }
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetValue(MRubyValue key, out MRubyValue value)
    {
        for (var i = 0; i < keys.Count; i++)
        {
            if (KeyEquals(key, keys[i]))
            {
                value = values[i];
                return true;
            }
        }
        value = default;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryDelete(MRubyValue key, out MRubyValue value, out bool modified)
    {
        var length = Length;
        for (var i = length - 1; i >= 0; i--)
        {
            if (length != Length)
            {
                value = default;
                modified = true;
                return false;
            }

            if (KeyEquals(key, keys[i]))
            {
                value = values[i];
                keys.RemoveAt(i);
                values.RemoveAt(i);
                modified = false;
                return true;
            }
        }
        value = default;
        modified = false;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Clear()
    {
        keys.Clear();
        values.Clear();
    }

    public void ReplaceTo(RHash other)
    {
        other.Clear();

        foreach (var k in Keys)
        {
            other.keys.Add(k);
        }
        foreach (var v in Values)
        {
            other.values.Add(v);
        }

        other.DefaultValue = DefaultValue;
        other.DefaultProc = DefaultProc;
    }

    public bool TryShift(out MRubyValue headKey, out MRubyValue headValue)
    {
        if (keys.Count > 0)
        {
            headKey = keys[0];
            headValue = values[0];
            keys.RemoveAt(0);
            values.RemoveAt(0);
            return true;
        }
        headValue = default;
        headKey = default;
        return false;
    }

    public RHash Dup() => new(keys, values, keyComparer, valueComparer, Class);

    internal override RObject Clone()
    {
        var clone = new RHash(Length, keyComparer, valueComparer, Class);
        InstanceVariables.CopyTo(clone.InstanceVariables);
        return clone;
    }

    public void Merge(RHash other)
    {
        if (this == other) return;
        if (other.Length == 0) return;

        for (var i = 0; i < other.keys.Count; i++)
        {
            this[other.keys[i]] = other.values[i];
        }
    }

    public struct Enumerator(RHash source) : IEnumerator<KeyValuePair<MRubyValue, MRubyValue>>
    {
        public KeyValuePair<MRubyValue, MRubyValue> Current { get; private set; }
        object IEnumerator.Current => Current;

        int index = -1;

        public bool MoveNext()
        {
            if (++index < source.keys.Count)
            {
                Current = new KeyValuePair<MRubyValue, MRubyValue>(source.keys[index], source.values[index]);
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    bool KeyEquals(MRubyValue a, MRubyValue b)
    {
        return keyComparer.Equals(a, b);
    }

    public Enumerator GetEnumerator() => new(this);

    IEnumerator<KeyValuePair<MRubyValue, MRubyValue>> IEnumerable<KeyValuePair<MRubyValue, MRubyValue>>.GetEnumerator() =>
        GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}