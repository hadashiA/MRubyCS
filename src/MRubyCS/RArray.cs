using System;
using System.Linq;
using System.Runtime.CompilerServices;

namespace MRubyCS;

public sealed class RArray : RObject
{
    public int Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private set;
    }

    public MRubyValue this[int index]
    {
        get
        {
            if (index < 0)
            {
                index += Length;
            }
            if ((uint)index < (uint)Length)
            {
                return data[offset + index];
            }
            return MRubyValue.Nil;
        }
        set
        {
            EnsureModifiable(index + 1);
            data[offset + index] = value;
        }
    }

    MRubyValue[] data;
    int offset;
    bool dataOwned;

    public Span<MRubyValue> AsSpan() => data.AsSpan(offset, Length);

    public Span<MRubyValue> AsSpan(int start, int count) =>
        data.AsSpan(offset + start, count);

    public Span<MRubyValue> AsSpan(int start) =>
        data.AsSpan(offset + start, Length - start);

    internal RArray(ReadOnlySpan<MRubyValue> values, RClass arrayClass)
        : base(MRubyVType.Array, arrayClass)
    {
        Length = values.Length;
        offset = 0;
        data = values.ToArray();
        dataOwned = true;
    }

    internal RArray(int capacity, RClass arrayClass) : base(MRubyVType.Array, arrayClass)
    {
        Length = 0;
        offset = 0;
        data = new MRubyValue[capacity];
        dataOwned = true;
    }

    RArray(RArray shared)
        : this(shared, 0, shared.Length)
    {
    }

    RArray(RArray shared, int offset, int size) : base(MRubyVType.Array, shared.Class)
    {
        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        if (size > shared.Length)
        {
            size = shared.Length;
        }
        Length = size;
        this.offset = offset;
        data = shared.data;
        dataOwned = false;
    }

    public override string ToString()
    {
        var list = AsSpan().ToArray().Select(x => x.ToString());
        return $"[{string.Join(", ", list)}]";
    }

    public RArray Dup() => new(this);

    public RArray SubSequence(int start, int length)
    {
        return new RArray(this, start, length);
    }

    public void Clear()
    {
        if (dataOwned)
        {
            AsSpan().Clear();
            Length = 0;
        }
        else
        {
            EnsureModifiable(0, true);
        }
    }

    public void Push(MRubyValue newItem)
    {
        var currentLength = Length;
        EnsureModifiable(currentLength + 1, true);
        data[currentLength] = newItem;
    }

    public bool TryPop(out MRubyValue value)
    {
        if (Length <= 0)
        {
            value = default;
            return false;
        }

        value = data[offset + Length - 1];
        EnsureModifiable(Length - 1, true);
        return true;
    }

    public void Unshift(MRubyValue newItem)
    {
        var src = AsSpan();
        if (data.Length <= Length)
        {
            data = new MRubyValue[Length * 2];
        }

        dataOwned = true;
        var dst = data.AsSpan(1, Length);
        src.CopyTo(dst);
        dst[0] = newItem;
        Length++;
    }

    public void Concat(RArray other)
    {
        if (Length <= 0)
        {
            Length = other.Length;
            data = other.data;
            dataOwned = false;
            return;
        }

        var currentLength = Length;
        var newLength = currentLength + other.Length;
        var source = other.AsSpan();
        EnsureModifiable(newLength, true);
        source.CopyTo(AsSpan(currentLength));
    }

    public MRubyValue DeleteAt(int index)
    {
        if (index < 0) index += Length;
        if (index < 0 || index >= Length) return MRubyValue.Nil;

        var value = data[offset + index];
        var src = AsSpan(index + 1);
        var dst = AsSpan(index);
        src.CopyTo(dst);
        Length--;
        return value;
    }

    public void CopyTo(RArray other)
    {
        if (other.Length < Length)
        {
            other.EnsureModifiable(Length);
        }
        AsSpan().CopyTo(other.AsSpan());
        other.Length = Length;
    }

    internal override RObject Clone()
    {
        var clone = new RArray(data.Length, Class);
        InstanceVariables.CopyTo(clone.InstanceVariables);
        return clone;
    }

    internal void PushRange(ReadOnlySpan<MRubyValue>newItems)
    {
        var start = Length;
        EnsureModifiable(Length + newItems.Length, true);
        newItems.CopyTo(data.AsSpan(start));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void EnsureModifiable(int capacity, bool expandLength = false)
    {
        if (data.Length - offset < capacity)
        {
            var newLength = data.Length * 2;
            if (newLength - offset < capacity)
            {
                newLength = capacity;
            }

            if (dataOwned)
            {
                Array.Resize(ref data, newLength);
            }
            else
            {
                var newData = new MRubyValue[newLength];
                data.CopyTo(newData, 0);
                data = newData;
                dataOwned = true;
            }
        }
        else if (!dataOwned)
        {
            data = AsSpan().ToArray();
            dataOwned = true;
        }

        if (expandLength)
        {
            Length = capacity;
        }
    }
}
