using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MRubyCS;

/// <summary>
/// Pluggable storage strategy for <see cref="RHash"/>. Concrete leaves own a
/// concrete backing layout. Mutations that don't fit a specialized leaf return
/// false from the Try* methods so the wrapper can demote to a generic
/// MRubyValue-keyed layout without breaking caller reference identity.
/// </summary>
abstract class RHashBackend
{
    public abstract int Length { get; }
    public abstract ReadOnlySpan<MRubyValue> Keys { get; }
    public abstract ReadOnlySpan<MRubyValue> Values { get; }

    public abstract MRubyValue Get(MRubyValue key);
    public abstract bool ContainsKey(MRubyValue key);
    public abstract bool TryGetValue(MRubyValue key, out MRubyValue value);

    /// <summary>Assigns the given key/value. Returns false if the key type
    /// does not fit this backend (caller must demote first).</summary>
    public abstract bool TrySet(MRubyValue key, MRubyValue value);

    /// <summary>Append-only insert. Returns false if the key type does not
    /// fit. Throws if the key already exists.</summary>
    public abstract bool TryAdd(MRubyValue key, MRubyValue value);

    public abstract bool TryDelete(MRubyValue key, out MRubyValue value);

    public abstract void Clear();

    public abstract bool TryShift(out MRubyValue headKey, out MRubyValue headValue);

    public abstract void Rehash();

    /// <summary>Iterate (key, value) pairs in insertion order.</summary>
    public abstract void GetPairAt(int index, out MRubyValue key, out MRubyValue value);

    /// <summary>Returns a generic backend with the same content. If this is
    /// already generic, returns self.</summary>
    public abstract RHashGenericBackend Demote();
}

internal sealed class RHashGenericBackend : RHashBackend
{
    readonly List<MRubyValue> keys;
    readonly List<MRubyValue> values;
    readonly Dictionary<MRubyValue, int> indexTable;
    readonly IEqualityComparer<MRubyValue> keyComparer;
    readonly IEqualityComparer<MRubyValue> valueComparer;

    public RHashGenericBackend(int capacity,
        IEqualityComparer<MRubyValue> keyComparer,
        IEqualityComparer<MRubyValue> valueComparer)
    {
        this.keyComparer = keyComparer;
        this.valueComparer = valueComparer;
        keys = new List<MRubyValue>(capacity);
        values = new List<MRubyValue>(capacity);
        indexTable = new Dictionary<MRubyValue, int>(capacity, keyComparer);
    }

    public IEqualityComparer<MRubyValue> KeyComparer => keyComparer;
    public IEqualityComparer<MRubyValue> ValueComparer => valueComparer;

    public override int Length => keys.Count;
    public override ReadOnlySpan<MRubyValue> Keys => CollectionsMarshal.AsSpan(keys);
    public override ReadOnlySpan<MRubyValue> Values => CollectionsMarshal.AsSpan(values);

    public override MRubyValue Get(MRubyValue key) =>
        indexTable.TryGetValue(key, out var i) ? values[i] : MRubyValue.Nil;

    public override bool ContainsKey(MRubyValue key) => indexTable.ContainsKey(key);

    public override bool TryGetValue(MRubyValue key, out MRubyValue value)
    {
        if (indexTable.TryGetValue(key, out var i))
        {
            value = values[i];
            return true;
        }
        value = default;
        return false;
    }

    public override bool TrySet(MRubyValue key, MRubyValue value)
    {
        if (indexTable.TryGetValue(key, out var i))
        {
            keys[i] = key;
            values[i] = value;
        }
        else
        {
            indexTable.Add(key, keys.Count);
            keys.Add(key);
            values.Add(value);
        }
        return true;
    }

    public override bool TryAdd(MRubyValue key, MRubyValue value)
    {
        if (indexTable.ContainsKey(key))
        {
            throw new InvalidOperationException("Duplicate key");
        }
        indexTable.Add(key, keys.Count);
        keys.Add(key);
        values.Add(value);
        return true;
    }

    public override bool TryDelete(MRubyValue key, out MRubyValue value)
    {
        if (indexTable.TryGetValue(key, out var idx))
        {
            value = values[idx];
            indexTable.Remove(key);
            keys.RemoveAt(idx);
            values.RemoveAt(idx);
            for (var i = idx; i < keys.Count; i++)
            {
                indexTable[keys[i]] = i;
            }
            return true;
        }
        value = default;
        return false;
    }

    public override void Clear()
    {
        keys.Clear();
        values.Clear();
        indexTable.Clear();
    }

    public override bool TryShift(out MRubyValue headKey, out MRubyValue headValue)
    {
        if (keys.Count == 0) { headKey = default; headValue = default; return false; }
        headKey = keys[0];
        headValue = values[0];
        TryDelete(headKey, out _);
        return true;
    }

    public override void Rehash()
    {
        indexTable.Clear();
        var writePos = 0;
        for (var readPos = 0; readPos < keys.Count; readPos++)
        {
            var key = keys[readPos];
            if (indexTable.TryGetValue(key, out var existingIndex))
            {
                values[existingIndex] = values[readPos];
            }
            else
            {
                if (writePos != readPos)
                {
                    keys[writePos] = keys[readPos];
                    values[writePos] = values[readPos];
                }
                indexTable[key] = writePos;
                writePos++;
            }
        }
        keys.RemoveRange(writePos, keys.Count - writePos);
        values.RemoveRange(writePos, values.Count - writePos);
    }

    public override void GetPairAt(int index, out MRubyValue key, out MRubyValue value)
    {
        key = keys[index];
        value = values[index];
    }

    public override RHashGenericBackend Demote() => this;
}

internal sealed class RHashSymbolKeyedBackend : RHashBackend
{
    readonly List<uint> keys;
    readonly List<MRubyValue> values;
    readonly Dictionary<uint, int> indexTable;

    /// <summary>Lazy cache exposed through <see cref="RHashBackend.Keys"/>.
    /// Invalidated on every key mutation.</summary>
    MRubyValue[]? keysSpanCache;

    public RHashSymbolKeyedBackend(int capacity)
    {
        keys = new List<uint>(capacity);
        values = new List<MRubyValue>(capacity);
        indexTable = new Dictionary<uint, int>(capacity);
    }

    public override int Length => keys.Count;

    public override ReadOnlySpan<MRubyValue> Keys
    {
        get
        {
            var n = keys.Count;
            if (keysSpanCache == null || keysSpanCache.Length != n)
            {
                keysSpanCache = new MRubyValue[n];
                for (var i = 0; i < n; i++)
                {
                    keysSpanCache[i] = new MRubyValue(new Symbol(keys[i]));
                }
            }
            return keysSpanCache;
        }
    }

    public override ReadOnlySpan<MRubyValue> Values => CollectionsMarshal.AsSpan(values);

    public override MRubyValue Get(MRubyValue key)
    {
        if (key.IsSymbol && indexTable.TryGetValue(key.SymbolValue.Value, out var i))
        {
            return values[i];
        }
        return MRubyValue.Nil;
    }

    public override bool ContainsKey(MRubyValue key) =>
        key.IsSymbol && indexTable.ContainsKey(key.SymbolValue.Value);

    public override bool TryGetValue(MRubyValue key, out MRubyValue value)
    {
        if (key.IsSymbol && indexTable.TryGetValue(key.SymbolValue.Value, out var i))
        {
            value = values[i];
            return true;
        }
        value = default;
        return false;
    }

    public override bool TrySet(MRubyValue key, MRubyValue value)
    {
        if (!key.IsSymbol) return false;
        var symId = key.SymbolValue.Value;
        if (indexTable.TryGetValue(symId, out var i))
        {
            keys[i] = symId;
            values[i] = value;
        }
        else
        {
            indexTable.Add(symId, keys.Count);
            keys.Add(symId);
            values.Add(value);
            keysSpanCache = null;
        }
        return true;
    }

    public override bool TryAdd(MRubyValue key, MRubyValue value)
    {
        if (!key.IsSymbol) return false;
        var symId = key.SymbolValue.Value;
        if (indexTable.ContainsKey(symId))
        {
            throw new InvalidOperationException("Duplicate key");
        }
        indexTable.Add(symId, keys.Count);
        keys.Add(symId);
        values.Add(value);
        keysSpanCache = null;
        return true;
    }

    public override bool TryDelete(MRubyValue key, out MRubyValue value)
    {
        if (!key.IsSymbol) { value = default; return false; }
        var symId = key.SymbolValue.Value;
        if (indexTable.TryGetValue(symId, out var idx))
        {
            value = values[idx];
            indexTable.Remove(symId);
            keys.RemoveAt(idx);
            values.RemoveAt(idx);
            for (var i = idx; i < keys.Count; i++)
            {
                indexTable[keys[i]] = i;
            }
            keysSpanCache = null;
            return true;
        }
        value = default;
        return false;
    }

    public override void Clear()
    {
        keys.Clear();
        values.Clear();
        indexTable.Clear();
        keysSpanCache = null;
    }

    public override bool TryShift(out MRubyValue headKey, out MRubyValue headValue)
    {
        if (keys.Count == 0) { headKey = default; headValue = default; return false; }
        headKey = new MRubyValue(new Symbol(keys[0]));
        headValue = values[0];
        TryDelete(headKey, out _);
        return true;
    }

    public override void Rehash()
    {
        indexTable.Clear();
        var writePos = 0;
        for (var readPos = 0; readPos < keys.Count; readPos++)
        {
            var key = keys[readPos];
            if (indexTable.TryGetValue(key, out var existingIndex))
            {
                values[existingIndex] = values[readPos];
            }
            else
            {
                if (writePos != readPos)
                {
                    keys[writePos] = keys[readPos];
                    values[writePos] = values[readPos];
                }
                indexTable[key] = writePos;
                writePos++;
            }
        }
        keys.RemoveRange(writePos, keys.Count - writePos);
        values.RemoveRange(writePos, values.Count - writePos);
        keysSpanCache = null;
    }

    public override void GetPairAt(int index, out MRubyValue key, out MRubyValue value)
    {
        key = new MRubyValue(new Symbol(keys[index]));
        value = values[index];
    }

    public override RHashGenericBackend Demote()
    {
        // Caller (RHash) supplies the comparer used for the generic side; we
        // rebuild against it.
        throw new InvalidOperationException(
            "RHashSymbolKeyedBackend.Demote() must be invoked via RHash to thread the comparers through.");
    }

    /// <summary>Demote variant that takes the comparers from the wrapper.</summary>
    public RHashGenericBackend DemoteWithComparers(
        IEqualityComparer<MRubyValue> keyComparer,
        IEqualityComparer<MRubyValue> valueComparer)
    {
        var dst = new RHashGenericBackend(keys.Count, keyComparer, valueComparer);
        for (var i = 0; i < keys.Count; i++)
        {
            dst.TryAdd(new MRubyValue(new Symbol(keys[i])), values[i]);
        }
        return dst;
    }
}
