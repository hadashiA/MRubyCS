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

    readonly object? body;
    public readonly MRubyMethodVisibility Visibility;
    public readonly MRubyMethodKind Kind;

    public RProc? Proc
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Kind == MRubyMethodKind.RProc ? Unsafe.As<RProc>(body!) : null;
    }

    public MRubyMethod(RProc proc, MRubyMethodVisibility visibility = MRubyMethodVisibility.Default)
    {
        body = proc;
        Kind = MRubyMethodKind.RProc;
        Visibility = visibility;
    }

    public MRubyMethod(MRubyFunc? func, MRubyMethodVisibility visibility = MRubyMethodVisibility.Default)
    {
        body = func;
        Kind = MRubyMethodKind.CSharpFunc;
        Visibility = visibility;
    }

    public MRubyMethod WithVisibility(MRubyMethodVisibility visibility) => Kind == MRubyMethodKind.RProc
        ? new MRubyMethod(Unsafe.As<RProc>(body!), visibility)
        : new MRubyMethod(Unsafe.As<MRubyFunc>(body!), visibility);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MRubyValue Invoke(MRubyState state, MRubyValue self)
    {
        return Unsafe.As<MRubyFunc>(body!).Invoke(state, self);
    }

    public bool Equals(MRubyMethod other)
    {
        return body == other.body;
    }

    public override bool Equals(object? obj)
    {
        return obj is MRubyMethod other && Equals(other);
    }

    public override int GetHashCode()
    {
        return body?.GetHashCode() ?? 0;
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
