using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace MRubyCS;

public sealed class RArray : RObject, IEnumerable<MRubyValue>
{
    public static int MaxLength => 0X7FFFFFC7;

    /// <summary>Pluggable backing storage. Replaceable on demote so the
    /// .NET object identity of <see cref="RArray"/> stays stable across
    /// type-mismatched mutations (Ruby in-place semantics).</summary>
    internal RArrayBackend backend;

    public int Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => backend.Length;
    }

    public MRubyValue this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (index < 0)
            {
                index += backend.Length;
            }
            if ((uint)index < (uint)backend.Length)
            {
                return backend.Get(index);
            }
            return MRubyValue.Nil;
        }
        set
        {
            backend.EnsureCapacity(index + 1, expandLength: index >= backend.Length);
            if (!backend.TrySet(index, value))
            {
                backend = backend.Demote();
                backend.TrySet(index, value);
            }
        }
    }

    /// <summary>Returns a writable span over the array's contents. If the
    /// array is currently backed by specialized primitive storage, calling
    /// this method transparently rebuilds an MRubyValue[] backing — the
    /// instance reverts to the generic representation thereafter.</summary>
    public Span<MRubyValue> AsSpan()
    {
        var obj = EnsureObjectBackend();
        return obj.AsSpan();
    }

    /// <inheritdoc cref="AsSpan()"/>
    public Span<MRubyValue> AsSpan(int start, int count)
    {
        var obj = EnsureObjectBackend();
        return obj.AsSpan(start, count);
    }

    /// <inheritdoc cref="AsSpan()"/>
    public Span<MRubyValue> AsSpan(int start)
    {
        var obj = EnsureObjectBackend();
        return obj.AsSpan(start);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    RArrayObjectBackend EnsureObjectBackend()
    {
        if (backend is RArrayObjectBackend obj) return obj;
        var demoted = backend.Demote();
        backend = demoted;
        return demoted;
    }

    internal RArray(ReadOnlySpan<MRubyValue> values, RClass arrayClass)
        : base(MRubyVType.Array, arrayClass)
    {
        backend = RArrayBackendFactory.FromSpan(values);
    }

    internal RArray(int capacity, RClass arrayClass) : base(MRubyVType.Array, arrayClass)
    {
        backend = new RArrayObjectBackend(capacity);
    }

    RArray(RArrayBackend sharedBackend, RClass arrayClass) : base(MRubyVType.Array, arrayClass)
    {
        backend = sharedBackend;
    }

    public override string ToString()
    {
        var list = AsSpan().ToArray().Select(x => x.ToString());
        return $"[{string.Join(", ", list)}]";
    }

    public RArray Dup() => new(backend.SubSequence(0, backend.Length), Class);

    public RArray SubSequence(int start, int length)
    {
        if (length > backend.Length) length = backend.Length;
        return new RArray(backend.SubSequence(start, length), Class);
    }

    public void Clear() => backend.Clear();

    public void Push(MRubyValue newItem)
    {
        if (!backend.TryPush(newItem))
        {
            backend = backend.Demote();
            backend.TryPush(newItem);
        }
    }

    public bool TryPop(out MRubyValue value) => backend.TryPop(out value);

    public MRubyValue Shift() => backend.Shift();

    public RArray Shift(int n)
    {
        if (backend.Length <= 0 || n <= 0) return new RArray(0, Class);
        if (n > backend.Length) n = backend.Length;

        var head = backend.SubSequence(0, n);
        // Advance self by n elements: take a sub-view of [n..end].
        backend = backend.SubSequence(n, backend.Length - n);
        return new RArray(head, Class);
    }

    public void Unshift(ReadOnlySpan<MRubyValue> newItems)
    {
        if (newItems.Length <= 0) return;

        // Unshift writes through AsSpan, which always demotes. Match the
        // pre-strategy code path: grow first, then move tail, then write head.
        var currentLength = backend.Length;
        var obj = EnsureObjectBackend();
        obj.EnsureCapacity(currentLength + newItems.Length, expandLength: true);
        var span = obj.AsSpan();
        obj.AsSpan(0, currentLength).CopyTo(obj.AsSpan(newItems.Length));
        newItems.CopyTo(span);
    }

    public void Concat(RArray other)
    {
        if (backend.Length <= 0)
        {
            // Empty-self alias trick: take a non-owned view of other's storage.
            backend = other.backend.AliasView();
            return;
        }

        var currentLength = backend.Length;
        var newLength = currentLength + other.backend.Length;

        // Same-backend-type fast path keeps both arrays specialized.
        if (backend.GetType() == other.backend.GetType())
        {
            backend.TryAppendSameType(other.backend, 0, other.backend.Length);
            return;
        }

        // Mixed types: demote both via AsSpan; capture the source first so
        // EnsureCapacity below doesn't invalidate it (also works for self
        // alias via Span's array-pinning).
        var src = other.AsSpan();
        var dst = EnsureObjectBackend();
        dst.EnsureCapacity(newLength, expandLength: true);
        src.CopyTo(dst.AsSpan(currentLength));
    }

    public MRubyValue DeleteAt(int index)
    {
        if (index < 0) index += backend.Length;
        if (index < 0 || index >= backend.Length) return MRubyValue.Nil;

        var value = backend.Get(index);
        backend.DeleteAt(index);
        return value;
    }

    public void CopyTo(RArray other)
    {
        var len = backend.Length;
        var src = AsSpan();
        var dst = other.EnsureObjectBackend();
        dst.EnsureCapacity(len, expandLength: true);
        src.CopyTo(dst.AsSpan());
    }

    public void ReplaceTo(RArray other)
    {
        other.backend.Clear();
        CopyTo(other);
    }

    internal override RObject Clone()
    {
        var clone = new RArray(backend.Capacity, Class);
        InstanceVariables.CopyTo(clone.InstanceVariables);
        return clone;
    }

    internal void PushRange(ReadOnlySpan<MRubyValue> newItems)
    {
        if (newItems.Length == 0) return;

        // Bulk-load promotion path: only if target is empty Generic AND the
        // span is homogeneous, replace the backend with a specialised one.
        if (backend is RArrayObjectBackend obj && obj.Length == 0 && obj.DataOwned)
        {
            var promoted = RArrayBackendFactory.FromSpan(newItems);
            if (promoted is not RArrayObjectBackend)
            {
                backend = promoted;
                return;
            }
        }

        if (!backend.TryPushRange(newItems))
        {
            backend = backend.Demote();
            backend.TryPushRange(newItems);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void MakeModifiable(int capacity, bool expandLength = false)
    {
        backend.EnsureCapacity(capacity, expandLength);
    }

    public struct Enumerator(RArray source) : IEnumerator<MRubyValue>
    {
        public MRubyValue Current { get; private set; }
        object IEnumerator.Current => Current;

        int index = -1;

        public bool MoveNext()
        {
            if (index + 1 < source.Length)
            {
                index++;
                Current = source[index];
                return true;
            }
            return false;
        }

        public void Reset()
        {
            index = -1;
            Current = default;
        }

        public void Dispose() { }
    }

    public Enumerator GetEnumerator() => new(this);

    IEnumerator<MRubyValue> IEnumerable<MRubyValue>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
