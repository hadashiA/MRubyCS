using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace MRubyCS;

public class VariableTable : IEnumerable<KeyValuePair<Symbol, MRubyValue>>
{
    Symbol[] keys = Array.Empty<Symbol>();
    MRubyValue[] values = Array.Empty<MRubyValue>();
    int count;

    public int Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Defined(Symbol id)
    {
        for (int i = 0; i < count; i++)
        {
            if (keys[i] == id) return true;
        }
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGet(Symbol id, out MRubyValue value)
    {
        for (int i = 0; i < count; i++)
        {
            if (keys[i] == id)
            {
                value = values[i];
                return true;
            }
        }
        value = default;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MRubyValue Get(Symbol id)
    {
        for (int i = 0; i < count; i++)
        {
            if (keys[i] == id) return values[i];
        }
        return MRubyValue.Nil;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Set(Symbol id, MRubyValue value)
    {
        for (int i = 0; i < count; i++)
        {
            if (keys[i] == id)
            {
                values[i] = value;
                return;
            }
        }
        if (count >= keys.Length) Grow();
        keys[count] = id;
        values[count] = value;
        count++;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Remove(Symbol id, out MRubyValue removedValue)
    {
        for (int i = 0; i < count; i++)
        {
            if (keys[i] == id)
            {
                removedValue = values[i];
                count--;
                for (int j = i; j < count; j++)
                {
                    keys[j] = keys[j + 1];
                    values[j] = values[j + 1];
                }
                keys[count] = default;
                values[count] = default;
                return true;
            }
        }
        removedValue = default;
        return false;
    }

    public void Clear()
    {
        if (count > 0)
        {
            Array.Clear(keys, 0, count);
            Array.Clear(values, 0, count);
            count = 0;
        }
    }

    public void CopyTo(VariableTable other)
    {
        if (count == 0) return;
        if (other.keys.Length < other.count + count)
        {
            var newSize = Math.Max(other.keys.Length == 0 ? 4 : other.keys.Length * 2, other.count + count);
            Array.Resize(ref other.keys, newSize);
            Array.Resize(ref other.values, newSize);
        }
        Array.Copy(keys, 0, other.keys, other.count, count);
        Array.Copy(values, 0, other.values, other.count, count);
        other.count += count;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    void Grow()
    {
        var newSize = keys.Length == 0 ? 4 : keys.Length * 2;
        Array.Resize(ref keys, newSize);
        Array.Resize(ref values, newSize);
    }

    public Enumerator GetEnumerator() => new(this);

    IEnumerator<KeyValuePair<Symbol, MRubyValue>> IEnumerable<KeyValuePair<Symbol, MRubyValue>>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public struct Enumerator : IEnumerator<KeyValuePair<Symbol, MRubyValue>>
    {
        readonly VariableTable table;
        int index;

        internal Enumerator(VariableTable table)
        {
            this.table = table;
            index = -1;
        }

        public KeyValuePair<Symbol, MRubyValue> Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => new(table.keys[index], table.values[index]);
        }

        object IEnumerator.Current => Current;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            return ++index < table.count;
        }

        public void Reset() => index = -1;
        public void Dispose() { }
    }
}
