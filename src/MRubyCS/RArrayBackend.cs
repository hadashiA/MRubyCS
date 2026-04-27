using System;
using System.Runtime.CompilerServices;

namespace MRubyCS;

/// <summary>
/// Pluggable storage strategy for <see cref="RArray"/>. Each leaf class owns a
/// concrete backing buffer (MRubyValue[], long[], or double[]) and answers
/// reads / writes against it. Mutations that don't fit a specialized backing
/// return false from the Try* methods so the wrapper can demote to a generic
/// MRubyValue[] backend without breaking caller reference identity.
/// </summary>
abstract class RArrayBackend
{
    protected int offset;

    /// <summary>Logical element count. Setter is protected so backends can update it.</summary>
    public int Length { get; protected set; }

    public bool DataOwned { get; protected set; }

    public abstract MRubyValue Get(int index);

    /// <summary>Assigns a value at the given index. Returns false if the value type
    /// does not fit this backend (caller must demote first).</summary>
    public abstract bool TrySet(int index, MRubyValue value);

    /// <summary>Appends a value. Returns false if the value type does not fit.</summary>
    public abstract bool TryPush(MRubyValue value);

    /// <summary>Bulk-append a homogeneous span. Returns false if any element does
    /// not fit; partial mutation is NOT performed in that case.</summary>
    public abstract bool TryPushRange(ReadOnlySpan<MRubyValue> items);

    /// <summary>Returns an Object backend with the same content. If this is
    /// already an Object backend, returns self.</summary>
    public abstract RArrayObjectBackend Demote();

    /// <summary>Grow the buffer if needed; if expandLength, also raises Length.</summary>
    public abstract void EnsureCapacity(int capacity, bool expandLength);

    /// <summary>Returns a non-owned (CoW-shared) view of [start..start+length] over
    /// the same underlying buffer. Type-preserving.</summary>
    public abstract RArrayBackend SubSequence(int start, int length);

    /// <summary>Returns a non-owned alias of self (same content, same buffer).
    /// Used by <c>Array#concat</c> for the empty-target alias trick.</summary>
    public abstract RArrayBackend AliasView();

    public abstract void Clear();

    public abstract bool TryPop(out MRubyValue value);

    public abstract void DeleteAt(int index);

    /// <summary>Same-type bulk append. Returns false if <paramref name="src"/> is
    /// a different backend type (caller should demote).</summary>
    public abstract bool TryAppendSameType(RArrayBackend src, int srcStart, int srcCount);

    /// <summary>Returns the backing buffer length (informational; for Clone capacity).</summary>
    public abstract int Capacity { get; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MRubyValue Shift()
    {
        if (Length == 0) return MRubyValue.Nil;
        var v = Get(0);
        offset++;
        Length--;
        return v;
    }
}

internal sealed class RArrayObjectBackend : RArrayBackend
{
    MRubyValue[] data;

    public RArrayObjectBackend(int capacity)
    {
        data = capacity == 0 ? Array.Empty<MRubyValue>() : new MRubyValue[capacity];
        Length = 0;
        offset = 0;
        DataOwned = true;
    }

    public RArrayObjectBackend(MRubyValue[] data, int offset, int length, bool dataOwned)
    {
        this.data = data;
        this.offset = offset;
        Length = length;
        DataOwned = dataOwned;
    }

    public RArrayObjectBackend(ReadOnlySpan<MRubyValue> values)
    {
        data = values.ToArray();
        Length = values.Length;
        offset = 0;
        DataOwned = true;
    }

    public override int Capacity => data.Length;

    public Span<MRubyValue> AsSpan() => data.AsSpan(offset, Length);
    public Span<MRubyValue> AsSpan(int start, int count) => data.AsSpan(offset + start, count);
    public Span<MRubyValue> AsSpan(int start) => data.AsSpan(offset + start, Length - start);

    /// <summary>Whole backing buffer past offset. Used by demote helpers that
    /// preserve trailing slots beyond Length (matching SpliceArray semantics).</summary>
    public Span<MRubyValue> RawTailFromOffset() => data.AsSpan(offset);

    public override MRubyValue Get(int index) => data[offset + index];

    public override bool TrySet(int index, MRubyValue value)
    {
        data[offset + index] = value;
        return true;
    }

    public override bool TryPush(MRubyValue value)
    {
        EnsureCapacity(Length + 1, expandLength: true);
        data[offset + Length - 1] = value;
        return true;
    }

    public override bool TryPushRange(ReadOnlySpan<MRubyValue> items)
    {
        if (items.Length == 0) return true;
        var start = Length;
        EnsureCapacity(Length + items.Length, expandLength: true);
        items.CopyTo(data.AsSpan(offset + start, items.Length));
        return true;
    }

    public override RArrayObjectBackend Demote() => this;

    public override void EnsureCapacity(int capacity, bool expandLength)
    {
        if (data.Length - offset < capacity)
        {
            var newSize = data.Length * 2;
            if (newSize - offset < capacity) newSize = capacity;

            if (DataOwned)
            {
                Array.Resize(ref data, newSize);
            }
            else
            {
                var newData = new MRubyValue[newSize];
                data.AsSpan(offset).CopyTo(newData);
                data = newData;
                offset = 0;
                DataOwned = true;
            }
        }
        else if (!DataOwned)
        {
            var newData = new MRubyValue[data.Length];
            data.AsSpan(offset).CopyTo(newData);
            data = newData;
            offset = 0;
            DataOwned = true;
        }

        if (expandLength) Length = capacity;
    }

    public override RArrayBackend SubSequence(int start, int length) =>
        new RArrayObjectBackend(data, offset + start, length, dataOwned: false);

    public override RArrayBackend AliasView() =>
        new RArrayObjectBackend(data, offset, Length, dataOwned: false);

    public override void Clear()
    {
        if (DataOwned)
        {
            data.AsSpan(offset, Length).Clear();
            Length = 0;
        }
        else
        {
            EnsureCapacity(0, expandLength: true);
        }
    }

    public override bool TryPop(out MRubyValue value)
    {
        if (Length == 0) { value = default; return false; }
        value = data[offset + Length - 1];
        Length--;
        return true;
    }

    public override void DeleteAt(int index)
    {
        var src = data.AsSpan(offset + index + 1, Length - index - 1);
        var dst = data.AsSpan(offset + index, Length - index - 1);
        src.CopyTo(dst);
        Length--;
    }

    public override bool TryAppendSameType(RArrayBackend src, int srcStart, int srcCount)
    {
        if (src is not RArrayObjectBackend obj) return false;
        var srcSpan = obj.data.AsSpan(obj.offset + srcStart, srcCount);
        EnsureCapacity(Length + srcCount, expandLength: true);
        srcSpan.CopyTo(data.AsSpan(offset + Length - srcCount, srcCount));
        return true;
    }
}

internal sealed class RArrayFixnumBackend : RArrayBackend
{
    /// <summary>NaN-boxed bits — stored exactly as <see cref="MRubyValue.RawBits"/>.
    /// Reading is a single MRubyValue field-assign with no bit-fiddle.</summary>
    long[] data;

    public RArrayFixnumBackend(long[] data, int offset, int length, bool dataOwned)
    {
        this.data = data;
        this.offset = offset;
        Length = length;
        DataOwned = dataOwned;
    }

    public override int Capacity => data.Length;

    public Span<long> RawSpan() => data.AsSpan(offset, Length);
    public Span<long> RawSpan(int start, int count) => data.AsSpan(offset + start, count);
    public Span<long> RawTailFromOffset() => data.AsSpan(offset);

    public override MRubyValue Get(int index) => new(data[offset + index], null);

    public override bool TrySet(int index, MRubyValue value)
    {
        if (!value.IsFixnum) return false;
        data[offset + index] = value.RawBits;
        return true;
    }

    public override bool TryPush(MRubyValue value)
    {
        if (!value.IsFixnum) return false;
        EnsureCapacity(Length + 1, expandLength: true);
        data[offset + Length - 1] = value.RawBits;
        return true;
    }

    public override bool TryPushRange(ReadOnlySpan<MRubyValue> items)
    {
        if (items.Length == 0) return true;
        for (var i = 0; i < items.Length; i++)
        {
            if (!items[i].IsFixnum) return false;
        }
        var start = Length;
        EnsureCapacity(Length + items.Length, expandLength: true);
        var dst = data.AsSpan(offset + start, items.Length);
        for (var i = 0; i < items.Length; i++) dst[i] = items[i].RawBits;
        return true;
    }

    public override RArrayObjectBackend Demote()
    {
        var src = data.AsSpan(offset);
        var dst = new MRubyValue[data.Length];
        for (var i = 0; i < src.Length; i++) dst[i] = new MRubyValue(src[i], null);
        return new RArrayObjectBackend(dst, 0, Length, true);
    }

    public override void EnsureCapacity(int capacity, bool expandLength)
    {
        if (data.Length - offset < capacity)
        {
            var newSize = data.Length * 2;
            if (newSize - offset < capacity) newSize = capacity;

            if (DataOwned)
            {
                Array.Resize(ref data, newSize);
            }
            else
            {
                var newData = new long[newSize];
                data.AsSpan(offset).CopyTo(newData);
                data = newData;
                offset = 0;
                DataOwned = true;
            }
        }
        else if (!DataOwned)
        {
            var newData = new long[data.Length];
            data.AsSpan(offset).CopyTo(newData);
            data = newData;
            offset = 0;
            DataOwned = true;
        }

        if (expandLength) Length = capacity;
    }

    public override RArrayBackend SubSequence(int start, int length) =>
        new RArrayFixnumBackend(data, offset + start, length, dataOwned: false);

    public override RArrayBackend AliasView() =>
        new RArrayFixnumBackend(data, offset, Length, dataOwned: false);

    public override void Clear()
    {
        if (DataOwned)
        {
            data.AsSpan(offset, Length).Clear();
            Length = 0;
        }
        else
        {
            EnsureCapacity(0, expandLength: true);
        }
    }

    public override bool TryPop(out MRubyValue value)
    {
        if (Length == 0) { value = default; return false; }
        value = new MRubyValue(data[offset + Length - 1], null);
        Length--;
        return true;
    }

    public override void DeleteAt(int index)
    {
        var src = data.AsSpan(offset + index + 1, Length - index - 1);
        var dst = data.AsSpan(offset + index, Length - index - 1);
        src.CopyTo(dst);
        Length--;
    }

    public override bool TryAppendSameType(RArrayBackend src, int srcStart, int srcCount)
    {
        if (src is not RArrayFixnumBackend fix) return false;
        var srcSpan = fix.data.AsSpan(fix.offset + srcStart, srcCount);
        EnsureCapacity(Length + srcCount, expandLength: true);
        srcSpan.CopyTo(data.AsSpan(offset + Length - srcCount, srcCount));
        return true;
    }
}

internal sealed class RArrayFloatBackend : RArrayBackend
{
    /// <summary>Raw doubles. Reading uses <see cref="MRubyValue(double)"/> which
    /// re-applies the NaN-box mask.</summary>
    double[] data;

    public RArrayFloatBackend(double[] data, int offset, int length, bool dataOwned)
    {
        this.data = data;
        this.offset = offset;
        Length = length;
        DataOwned = dataOwned;
    }

    public override int Capacity => data.Length;

    public Span<double> RawSpan() => data.AsSpan(offset, Length);
    public Span<double> RawTailFromOffset() => data.AsSpan(offset);

    public override MRubyValue Get(int index) => new MRubyValue(data[offset + index]);

    public override bool TrySet(int index, MRubyValue value)
    {
        if (!value.IsFloat) return false;
        data[offset + index] = value.FloatValue;
        return true;
    }

    public override bool TryPush(MRubyValue value)
    {
        if (!value.IsFloat) return false;
        EnsureCapacity(Length + 1, expandLength: true);
        data[offset + Length - 1] = value.FloatValue;
        return true;
    }

    public override bool TryPushRange(ReadOnlySpan<MRubyValue> items)
    {
        if (items.Length == 0) return true;
        for (var i = 0; i < items.Length; i++)
        {
            if (!items[i].IsFloat) return false;
        }
        var start = Length;
        EnsureCapacity(Length + items.Length, expandLength: true);
        var dst = data.AsSpan(offset + start, items.Length);
        for (var i = 0; i < items.Length; i++) dst[i] = items[i].FloatValue;
        return true;
    }

    public override RArrayObjectBackend Demote()
    {
        var src = data.AsSpan(offset);
        var dst = new MRubyValue[data.Length];
        for (var i = 0; i < src.Length; i++) dst[i] = new MRubyValue(src[i]);
        return new RArrayObjectBackend(dst, 0, Length, true);
    }

    public override void EnsureCapacity(int capacity, bool expandLength)
    {
        if (data.Length - offset < capacity)
        {
            var newSize = data.Length * 2;
            if (newSize - offset < capacity) newSize = capacity;

            if (DataOwned)
            {
                Array.Resize(ref data, newSize);
            }
            else
            {
                var newData = new double[newSize];
                data.AsSpan(offset).CopyTo(newData);
                data = newData;
                offset = 0;
                DataOwned = true;
            }
        }
        else if (!DataOwned)
        {
            var newData = new double[data.Length];
            data.AsSpan(offset).CopyTo(newData);
            data = newData;
            offset = 0;
            DataOwned = true;
        }

        if (expandLength) Length = capacity;
    }

    public override RArrayBackend SubSequence(int start, int length) =>
        new RArrayFloatBackend(data, offset + start, length, dataOwned: false);

    public override RArrayBackend AliasView() =>
        new RArrayFloatBackend(data, offset, Length, dataOwned: false);

    public override void Clear()
    {
        if (DataOwned)
        {
            data.AsSpan(offset, Length).Clear();
            Length = 0;
        }
        else
        {
            EnsureCapacity(0, expandLength: true);
        }
    }

    public override bool TryPop(out MRubyValue value)
    {
        if (Length == 0) { value = default; return false; }
        value = new MRubyValue(data[offset + Length - 1]);
        Length--;
        return true;
    }

    public override void DeleteAt(int index)
    {
        var src = data.AsSpan(offset + index + 1, Length - index - 1);
        var dst = data.AsSpan(offset + index, Length - index - 1);
        src.CopyTo(dst);
        Length--;
    }

    public override bool TryAppendSameType(RArrayBackend src, int srcStart, int srcCount)
    {
        if (src is not RArrayFloatBackend f) return false;
        var srcSpan = f.data.AsSpan(f.offset + srcStart, srcCount);
        EnsureCapacity(Length + srcCount, expandLength: true);
        srcSpan.CopyTo(data.AsSpan(offset + Length - srcCount, srcCount));
        return true;
    }
}

/// <summary>Static factory helpers for picking the right backend at construct time.</summary>
internal static class RArrayBackendFactory
{
    public static RArrayBackend FromSpan(ReadOnlySpan<MRubyValue> values)
    {
        if (values.Length == 0)
        {
            return new RArrayObjectBackend(0);
        }

        var first = values[0];
        if (first.IsFixnum)
        {
            for (var i = 1; i < values.Length; i++)
            {
                if (!values[i].IsFixnum) return new RArrayObjectBackend(values);
            }
            var buf = new long[values.Length];
            for (var i = 0; i < values.Length; i++) buf[i] = values[i].RawBits;
            return new RArrayFixnumBackend(buf, 0, values.Length, true);
        }
        if (first.IsFloat)
        {
            for (var i = 1; i < values.Length; i++)
            {
                if (!values[i].IsFloat) return new RArrayObjectBackend(values);
            }
            var buf = new double[values.Length];
            for (var i = 0; i < values.Length; i++) buf[i] = values[i].FloatValue;
            return new RArrayFloatBackend(buf, 0, values.Length, true);
        }

        return new RArrayObjectBackend(values);
    }
}
