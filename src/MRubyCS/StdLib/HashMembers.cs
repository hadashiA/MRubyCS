namespace MRubyCS.StdLib;

static class HashMembers
{
    [MRubyMethod]
    public static MRubyMethod Initialize = new((state, self) =>
    {
        var hash = self.As<RHash>();
        var block = state.GetBlockArgument();
        if (state.TryGetArgumentAt(0, out var ifnone))
        {
            hash.DefaultValue = ifnone;
        }
        else if (block.Object is RProc proc)
        {
            hash.DefaultProc = proc;
        }
        return self;
    });

    [MRubyMethod]
    public static MRubyMethod InitializeCopy = new((state, self) =>
    {
        var hash = self.As<RHash>();
        var other = state.GetArgumentAsHashAt(0);

        if (hash != other)
        {
            other.ReplaceTo(hash);
        }
        return self;
    });

    public static MRubyMethod ToS = new((state, self) =>
    {
        var hash = self.As<RHash>();
        var result = state.NewString("{"u8);
        if (state.IsRecursiveCalling(Names.Inspect, self))
        {
            result.Concat("...}"u8);
        }
        else
        {
            var first = true;
            foreach (var (key, value) in hash)
            {
                if (!first)
                {
                    result.Concat(", "u8);
                }
                first = false;

                var keyString = state.Inspect(key);
                result.Concat(keyString);
                result.Concat("=>"u8);
                var valueString = state.Inspect(value);
                result.Concat(valueString);
            }
            result.Concat("}"u8);
        }
        return MRubyValue.From(result);
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod OpAref = new((state, self) =>
    {
        var hash = self.As<RHash>();
        var key = state.GetArgumentAt(0);
        if (hash.TryGetValue(key, out var value))
        {
            return value;
        }
        return state.Send(self, Names.Default);
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod OpAset = new((state, self) =>
    {
        var hash = self.As<RHash>();
        state.EnsureNotFrozen(hash);

        var key = state.GetArgumentAt(0);
        if (key.Object is RString { IsFrozen: false })
        {
            key = state.DupObject(key);
            key.Object?.MarkAsFrozen();
        }

        var value = state.GetArgumentAt(1);
        hash[key] = value;
        return value;
    });
    //
    // [MRubyMethod(RequiredArguments = 1)]
    // public static MRubyMethod OpEq = new((state, self) =>
    // {
    //     var hash = self.As<RHash>();
    //     var arg = state.GetArgumentAt(0);
    //     if (arg.Object is not RHash other || hash.Length != other.Length)
    //     {[8
    //         return MRubyValue.False;
    //     }
    //
    //     if (hash == other)
    //     {
    //         return MRubyValue.True;
    //     }
    //
    //     foreach (var (key, value) in hash)
    //     {
    //         if (other.TryGetValue(key, out var otherValue))
    //         {
    //             var valueEquals = state.Send(value, Names.OpEq, otherValue);
    //             if (valueEquals.Falsy) return MRubyValue.False;
    //         }
    //         else
    //         {
    //             return MRubyValue.False;
    //         }
    //     }
    //     return MRubyValue.True;
    // });
    //
    // [MRubyMethod(RequiredArguments = 1)]
    // public static MRubyMethod Eql = new((state, self) =>
    // {
    //     var hash = self.As<RHash>();
    //     var arg = state.GetArgumentAt(0);
    //     if (arg.Object is not RHash other || hash.Length != other.Length)
    //     {
    //         return MRubyValue.False;
    //     }
    //
    //     if (hash == other)
    //     {
    //         return MRubyValue.True;
    //     }
    //
    //     foreach (var (key, value) in hash)
    //     {
    //         if (other.TryGetValue(key, out var otherValue))
    //         {
    //             var valueEquals = state.Send(value, Names.QEql, otherValue);
    //             if (valueEquals.Falsy) return MRubyValue.False;
    //         }
    //         else
    //         {
    //             return MRubyValue.False;
    //         }
    //     }
    //     return MRubyValue.True;
    // });

    [MRubyMethod]
    public static MRubyMethod Size = new((state, self) =>
    {
        var h = self.As<RHash>();
        return MRubyValue.From(h.Length);
    });

    [MRubyMethod]
    public static MRubyMethod Keys = new((state, self) =>
    {
        var h = self.As<RHash>();
        var result = state.NewArray(h.Length);
        foreach (var key in h.Keys)
        {
            result.Push(key);
        }
        return MRubyValue.From(result);
    });

    [MRubyMethod]
    public static MRubyMethod Values = new((state, self) =>
    {
        var h = self.As<RHash>();
        var result = state.NewArray(h.Length);
        foreach (var value in h.Values)
        {
            result.Push(value);
        }
        return MRubyValue.From(result);
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod HasKey = new((state, self) =>
    {
        var h = self.As<RHash>();
        var key = state.GetArgumentAt(0);
        return MRubyValue.From(h.ContainsKey(key));
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod HasValue = new((state, self) =>
    {
        var h = self.As<RHash>();
        var value = state.GetArgumentAt(0);
        return MRubyValue.From(h.ContainsValue(value));
    });

    // [MRubyMethod(RequiredArguments = 1)]
    // public static MRubyMethod ToA = new((state, self) =>
    // {
    //     var h = self.As<RHash>();
    //     var result = state.NewArray(h.Length);
    //     for (var i = 0; i < h.Length; i++)
    //     {
    //         var entry = state.NewArray(2);
    //         entry.Push(h.Keys[i]);
    //         entry.Push(h.Values[i]);
    //         result.Push(MRubyValue.From(entry));
    //     }
    //     return MRubyValue.From(result);
    // });

    [MRubyMethod(OptionalArguments = 1)]
    public static MRubyMethod Default = new((state, self) =>
    {
        var h = self.As<RHash>();
        // var argc = state.GetArgumentCount();

        if (h.DefaultProc is { } proc)
        {
            state.TryGetArgumentAt(0, out var key);
            return state.Send(MRubyValue.From(proc), Names.Call, key);
        }
        if (h.DefaultValue.HasValue)
        {
            return h.DefaultValue.Value;
        }
        return MRubyValue.Nil;
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod SetDefault = new((state, self) =>
    {
        var h = self.As<RHash>();
        var value = state.GetArgumentAt(0);
        h.DefaultValue = value;
        return value;
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod Delete = new((state, self) =>
    {
        var h = self.As<RHash>();
        state.EnsureNotFrozen(h);

        var key = state.GetArgumentAt(0);
        h.TryDelete(key, out var value);
        return value;
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod Clear = new((state, self) =>
    {
        var h = self.As<RHash>();
        state.EnsureNotFrozen(h);

        h.Clear();
        return self;
    });

    [MRubyMethod]
    public static MRubyMethod Shift = new((state, self) =>
    {
        var h = self.As<RHash>();
        if (h.TryShift(out var headKey, out var headValue))
        {
            var result = state.NewArray(2);
            result.Push(headKey);
            result.Push(headValue);
            return MRubyValue.From(result);
        }
        return MRubyValue.Nil;
    });

    static MRubyValue GetDefaultValue(MRubyState state, RHash hash, MRubyValue key)
    {
        if (hash.DefaultValue.HasValue)
        {
            return hash.DefaultValue.Value;
        }
        if (hash.DefaultProc is { } proc)
        {
            return state.Send(MRubyValue.From(proc), Names.Call, key);
        }
        return MRubyValue.Nil;
    }
}