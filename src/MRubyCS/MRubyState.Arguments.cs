using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace MRubyCS;

partial class MRubyState
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsRecursiveCalling(Symbol methodId, MRubyValue self) =>
        context.IsRecursiveCalling(methodId, self);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsRecursiveCalling(Symbol methodId, MRubyValue self, MRubyValue arg0) =>
        context.IsRecursiveCalling(methodId, self, arg0);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetArgumentCount() => context.GetArgumentCount();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetKeywordArgumentCount() => context.GetKeywordArgumentCount();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MRubyValue GetArgumentAt(int index) => context.GetArgumentAt(index);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MRubyValue GetKeywordArgument(Symbol key) => context.GetKeywordArgument(key);

    public bool TryGetArgumentAt(int index, out MRubyValue value) =>
        context.TryGetArgumentAt(index, out value);
    public bool TryGetKeywordArgument(Symbol key, out MRubyValue value) =>
        context.TryGetKeywordArgument(key, out value);
    public MRubyValue GetSelf() => context.GetSelf();

    public ReadOnlySpan<KeyValuePair<Symbol, MRubyValue>> GetKeywordArguments() =>
        context.GetKeywordArgs(ref context.CurrentCallInfo);

    public RClass GetArgumentAsClassAt(int index)
    {
        var arg = GetArgumentAt(index);
        EnsureClassOrModule(arg);
        return arg.As<RClass>();
    }

    public Symbol GetArgumentAsSymbolAt(int index)
    {
        var arg = GetArgumentAt(index);
        return ToSymbol(arg);
    }

    public long GetArgumentAsIntegerAt(int index)
    {
        var arg = GetArgumentAt(index);
        return ToInteger(arg);
    }

    public double GetArgumentAsFloatAt(int index)
    {
        var arg = GetArgumentAt(index);
        return ToFloat(arg);
    }

    public RString GetArgumentAsStringAt(int index)
    {
        var arg = GetArgumentAt(index);
        if (arg.Object is RString str)
        {
            return str;
        }
        Raise(Names.TypeError, NewString($"{StringifyAny(arg)} cannot be converted to String"));
        return default!;
    }

    public RArray GetArgumentAsArrayAt(int index)
    {
        var arg = GetArgumentAt(index);
        if (arg.Object is RArray array)
        {
            return array;
        }
        Raise(Names.TypeError, NewString($"{StringifyAny(arg)} cannot be converted to Array"));
        return default!;
    }

    public ReadOnlySpan<MRubyValue> GetRestArgumentsAfter(int startIndex) =>
        context.GetRestArgumentsAfter(startIndex);

    public MRubyValue GetBlockArgument(bool optional = true)
    {
        var blockArg = context.GetBlockArgument();
        if (!optional && blockArg.IsNil)
        {
            Raise(Names.ArgumentError, "no block given"u8);
        }
        return blockArg;
    }
}