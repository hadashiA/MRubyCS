namespace MRubyCS.StdLib;

static class HashMembers
{
    [MRubyMethod(OptionalArguments = 1, BlockArgument = true)]
    public static MRubyMethod Initialize = new((state, self) =>
    {
        var hash = self.As<RHash>();
        state.EnsureArgumentCount(0, 1);
        var block = state.GetBlockArgument();
        if (state.TryGetArgumentAt(0, out var defaultValue))
        {
            if (block != null)
            {
                state.Raise(Names.ArgumentError, "invalid block"u8);
            }

            hash.DefaultValue = defaultValue;
        }
        else if (block != null)
        {
            hash.DefaultProc = block;
        
        return self;
    });

    [MRubyMethod]
    public static MRubyMethod InitializeCopy = new((state, self) =>
    {
        var hash = self.As<RHash>();
        state.EnsureNotFrozen(hash);

        var other = state.GetArgumentAsHashAt(0);

        if (hash != other)
        {
            other.ReplaceTo(hash);
        }

        return self;
    });

    public static MRubyMethod Inspect = new((state, self) =>
    {
        var hash = self.As<RHash>();
        var result = state.NewString("{"u8);

        // Currently, the only clue for checking whether a method is being called recursively is the method ID.
        // To prepare for cases where this method is called by an alias such as to_s, unify the current method ID to inspect.
        state.ModifyCurrentMethodId(Names.Inspect);

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
                if (key.IsSymbol)
                {
                    result.Concat(state.NameOf(key.SymbolValue));
                    result.Concat(": "u8);
                }
                else
                {
                    result.Concat(keyString);
                    result.Concat(" => "u8);
                }
                var valueString = state.Inspect(value);
                result.Concat(valueString);
            }

            result.Concat("}"u8);
        }

        return result;
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

        return state.Send(self, Names.Default, key);
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

    [MRubyMethod]
    public static MRubyMethod Size = new((state, self) =>
    {
        var h = self.As<RHash>();
        return h.Length;
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

        return result;
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

        return result;
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod HasKey = new((state, self) =>
    {
        var h = self.As<RHash>();
        var key = state.GetArgumentAt(0);
        return h.ContainsKey(key);
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod HasValue = new((state, self) =>
    {
        var h = self.As<RHash>();
        var value = state.GetArgumentAt(0);
        return h.ContainsValue(value);
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod Empty = new((state, self) =>
    {
        var h = self.As<RHash>();
        return h.Length <= 0;
    });

    [MRubyMethod(OptionalArguments = 1)]
    public static MRubyMethod Default = new((state, self) =>
    {
        var h = self.As<RHash>();
        state.EnsureArgumentCount(0, 1);

        if (h.DefaultProc is { } proc && state.TryGetArgumentAt(0, out var key))
        {
            return state.Send(proc, Names.Call, self, key);
        }

        if (h.DefaultValue.HasValue)
        {
            return h.DefaultValue.Value;
        }

        return MRubyValue.Nil;
    });

    [MRubyMethod(OptionalArguments = 1)]
    public static MRubyMethod DefaultProc = new((state, self) =>
    {
        var h = self.As<RHash>();
        if (h.DefaultProc is { } proc)
        {
            return proc;
        }

        return MRubyValue.Nil;
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod SetDefault = new((state, self) =>
    {
        var h = self.As<RHash>();
        state.EnsureNotFrozen(h);
        var value = state.GetArgumentAt(0);
        h.DefaultValue = value;
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
        state.EnsureNotFrozen(h);

        if (h.TryShift(out var headKey, out var headValue))
        {
            var result = state.NewArray(2);
            result.Push(headKey);
            result.Push(headValue);
            return result;
        }

        return MRubyValue.Nil;
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod Assoc = new((state, self) =>
    {
        var h = self.As<RHash>();
        var searchKey = state.GetArgumentAt(0);
        foreach (var x in h)
        {
            if (state.ValueEquals(searchKey, x.Key))
            {
                var result = state.NewArray(2);
                result.Push(x.Key);
                result.Push(x.Value);
                return result;
            }
        }
        return MRubyValue.Nil;
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod RAssoc = new((state, self) =>
    {
        var h = self.As<RHash>();

        var searchValue = state.GetArgumentAt(0);
        foreach (var x in h)
        {
            if (state.ValueEquals(searchValue, x.Value))
            {
                var result = state.NewArray(2);
                result.Push(x.Key);
                result.Push(x.Value);
                return result;
            }
        }
        return MRubyValue.Nil;
    });

    [MRubyMethod]
    public static MRubyMethod Rehash = new((state, self) =>
    {
        var h = self.As<RHash>();
        h.Rehash();
        return self;
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod InternalDelete = new((state, self) =>
    {
        var h = self.As<RHash>();

        state.EnsureNotFrozen(h);
        state.EnsureArgumentCount(1);

        var key = state.GetArgumentAt(0);
        h.TryDelete(key, out var value);
        return value;
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod InternalMerge = new((state, self) =>
    {
        var h = self.As<RHash>();
        var args = state.GetRestArgumentsAfter(0);
        foreach (var arg in args)
        {
            state.EnsureValueType(arg, MRubyVType.Hash);
            var other = arg.As<RHash>();
            if (h == other) continue;
            foreach (var entry in other)
            {
                h[entry.Key] = entry.Value;
            }
        }
        return self;
    });
}
