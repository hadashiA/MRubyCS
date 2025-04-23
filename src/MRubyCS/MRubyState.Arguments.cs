using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace MRubyCS;

partial class MRubyState
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsRecursiveCalling(RObject receiver, Symbol methodId) =>
        context.IsRecursiveCalling(receiver, methodId);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetArgumentCount() => context.GetArgumentCount();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetKeywordArgumentCount() => context.GetKeywordArgumentCount();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MRubyValue GetArg(int index) => context.GetArg(index);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MRubyValue GetKeywordArg(Symbol key) => context.GetKeywordArg(key);

    public bool TryGetArg(int index, out MRubyValue value) => context.TryGetArg(index, out value);
    public bool TryGetKeywordArg(Symbol key, out MRubyValue value) => context.TryGetKeywordArg(key, out value);
    public MRubyValue GetSelf() => context.GetSelf();

    public ReadOnlySpan<KeyValuePair<Symbol, MRubyValue>> GetKeywordArgs() =>
        context.GetKeywordArgs(ref context.CurrentCallInfo);

    public RClass GetArgAsClass(int index)
    {
        var arg = GetArg(index);
        EnsureClassOrModule(arg);
        return arg.As<RClass>();
    }

    public Symbol GetArgAsSymbol(int index)
    {
        var arg = GetArg(index);
        return ToSymbol(arg);
    }

    public long GetArgAsInteger(int index)
    {
        var arg = GetArg(index);
        return ToInteger(arg);
    }

    public double GetArgAsFloat(int index)
    {
        var arg = GetArg(index);
        return ToFloat(arg);
    }

    public RString GetArgAsString(int index)
    {
        var arg = GetArg(index);
        if (arg.VType != MRubyVType.String)
        {
            Raise(Names.TypeError, NewString($"{StringifyAny(arg)} cannot be converted to String"));
        }
        return arg.As<RString>();
    }

    public RArray GetArgAsArray(int index)
    {
        var arg = GetArg(index);
        if (arg.VType != MRubyVType.Array)
        {
            Raise(Names.TypeError, NewString($"{StringifyAny(arg)} cannot be converted to Array"));
        }
        return arg.As<RArray>();
    }

    public ReadOnlySpan<MRubyValue> GetRestArg(int startIndex) =>
        context.GetRestArg(startIndex);

    public MRubyValue GetBlockArg(bool optional = true)
    {
        var blockArg = context.GetBlockArg();
        if (!optional && blockArg.IsNil)
        {
            Raise(Names.ArgumentError, "no block given"u8);
        }
        return blockArg;
    }
}