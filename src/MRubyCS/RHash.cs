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
    readonly IEqualityComparer<MRubyValue> comparer;

    internal RHash(int capacity, IEqualityComparer<MRubyValue> comparer, RClass hashClass) : base(MRubyVType.Hash, hashClass)
    {
        keys = new List<MRubyValue>(capacity);
        values = new List<MRubyValue>(capacity);
        this.comparer = comparer;
    }

    RHash(List<MRubyValue> keys, List<MRubyValue> values, IEqualityComparer<MRubyValue> comparer, RClass hashClass) : base(MRubyVType.Hash, hashClass)
    {
        this.keys = keys;
        this.values = values;
        this.comparer = comparer;
    }

    public MRubyValue this[MRubyValue key]
    {
        get
        {
            for (var i = 0; i < keys.Count; i++)
            {
                if (KeyEquals(keys[i], key))
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
                if (KeyEquals(keys[i], key))
                {
                    values[i] = value;
                    return;
                }
            }
            keys.Add(key);
            values.Add(value);
        }
    }

    public MRubyValue GetOrDefault(MRubyValue key, MRubyState state)
    {
        for (var i = 0; i < keys.Count; i++)
        {
            if (KeyEquals(keys[i], key))
            {
                return values[i];
            }
        }
        return state.Send(MRubyValue.From(this), Names.Default);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(MRubyValue key, MRubyValue value)
    {
        if (ContainsKey(key))
        {
            throw new InvalidOperationException("Duplicate key");
        }
        keys.Add(key);
        values.Add(key);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ContainsKey(MRubyValue key)
    {
        foreach (var k in keys)
        {
            if (KeyEquals(key, k)) return true;
        }
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ContainsValue(MRubyValue value)
    {
        foreach (var v in values)
        {
            if (KeyEquals(value, v)) return true;
        }
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetValue(MRubyValue key, out MRubyValue value)
    {
        for (var i = 0; i < keys.Count; i++)
        {
            if (KeyEquals(keys[i], key))
            {
                value = values[i];
                return true;
            }
        }
        value = default;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryDelete(MRubyValue key, out MRubyValue value)
    {
        for (var i = keys.Count - 1; i >= 0; i--)
        {
            if (KeyEquals(keys[i], key))
            {
                value = values[i];
                keys.RemoveAt(i);
                values.RemoveAt(i);
                return true;
            }
        }
        value = default;
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

    public RHash Dup() => new(keys, values, comparer, Class);

    internal override RObject Clone()
    {
        var clone = new RHash(Length, comparer, Class);
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
        if (a.IsSymbol)
        {
            if (!b.IsSymbol) return false;
            return a.SymbolValue == b.SymbolValue;
        }

        if (a.IsInteger)
        {
            if (!b.IsInteger) return false;
            return a.IntegerValue == b.IntegerValue;
        }

        if (a.IsFloat)
        {
            if (!b.IsFloat) return false;
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            return a.FloatValue == b.FloatValue;
        }

        if (a.Object is RString strA)
        {
            if (b.Object is RString strB)
            {
                return strA.Equals(strB);
            }
            return false;
        }
        return comparer.Equals(a, b);
    }

    public Enumerator GetEnumerator() => new(this);

    IEnumerator<KeyValuePair<MRubyValue, MRubyValue>> IEnumerable<KeyValuePair<MRubyValue, MRubyValue>>.GetEnumerator() =>
        GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}