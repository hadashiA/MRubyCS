using System;
using System.Runtime.CompilerServices;

namespace MRubyCS;

public delegate MRubyValue MRubyFunc(MRubyState state, MRubyValue self);

public enum MRubyMethodKind
{
    RProc,
    CSharpFunc,
}

public enum MRubyMethodVisibility
{
    Default,
    Public,
    Private,
    Protected,
}

public readonly struct MRubyMethod : IEquatable<MRubyMethod>
{
    public static readonly MRubyMethod Nop = new((_, _) => MRubyValue.Nil);
    public static readonly MRubyMethod Undef = new((_, _) => MRubyValue.Nil);
    public static readonly MRubyMethod True = new((_, _) => MRubyValue.True);
    public static readonly MRubyMethod False = new((_, _) => MRubyValue.False);
    public static readonly MRubyMethod Identity = new((_, self) => self);

    // Union: stores either RProc or MRubyFunc depending on Kind
    readonly object? procOrFunc;

    // Bit-packed flags: bits 0-1 = Visibility (4 values), bit 2 = Kind (2 values)
    readonly byte flags;

    public RProc? Proc
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Kind == MRubyMethodKind.RProc ? Unsafe.As<object, RProc>(ref Unsafe.AsRef(in procOrFunc)!) : null;
    }

    public MRubyMethodVisibility Visibility
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (MRubyMethodVisibility)(flags & 0x3);
    }

    public MRubyMethodKind Kind
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (MRubyMethodKind)((flags >> 2) & 0x1);
    }

    public MRubyMethod(RProc proc, MRubyMethodVisibility visibility = MRubyMethodVisibility.Default)
    {
        procOrFunc = proc;
        flags = (byte)((int)visibility | ((int)MRubyMethodKind.RProc << 2));
    }

    public MRubyMethod(MRubyFunc? func, MRubyMethodVisibility visibility = MRubyMethodVisibility.Default)
    {
        procOrFunc = func;
        flags = (byte)((int)visibility | ((int)MRubyMethodKind.CSharpFunc << 2));
    }

    public MRubyMethod WithVisibility(MRubyMethodVisibility visibility) => Kind == MRubyMethodKind.RProc
        ? new MRubyMethod((RProc)procOrFunc!, visibility)
        : new MRubyMethod((MRubyFunc?)procOrFunc, visibility);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MRubyValue Invoke(MRubyState state, MRubyValue self)
    {
        return Unsafe.As<object, MRubyFunc>(ref Unsafe.AsRef(in procOrFunc)!).Invoke(state, self);
    }

    public bool Equals(MRubyMethod other)
    {
        return procOrFunc == other.procOrFunc;
    }

    public override bool Equals(object? obj)
    {
        return obj is MRubyMethod other && Equals(other);
    }

    public override int GetHashCode()
    {
        return procOrFunc?.GetHashCode() ?? 0;
    }

    public static bool operator ==(MRubyMethod left, MRubyMethod right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(MRubyMethod left, MRubyMethod right)
    {
        return !(left == right);
    }
}
