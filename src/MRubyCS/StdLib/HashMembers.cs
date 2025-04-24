namespace MRubyCS.StdLib;

public class HashMembers
{
    [MRubyMethod]
    public static MRubyMethod Initialize = new((state, self) =>
    {
        var hash = self.As<RHash>();
        var block = state.GetBlockArg();
        if (state.TryGetArg(0, out var ifnone))
        {
            hash.DefaultValue = ifnone;
        }
        else if (block.Object is RProc proc)
        {
            hash.DefaultProc = proc;
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
        var key = state.GetArg(0);
        if (hash.TryGetValue(key, out var value))
        {
            return value;
        }
        return Default(state, hash, key);
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod OpEq = new((state, self) =>
    {
        var hash = self.As<RHash>();
        var arg = state.GetArg(0);
        if (arg.Object is not RHash other || hash.Length != other.Length)
        {
            return MRubyValue.False;
        }

        if (hash == other)
        {
            return MRubyValue.True;
        }

        foreach (var (key, value) in hash)
        {
            if (other.TryGetValue(key, out var otherValue))
            {
                var valueEquals = state.Send(value, Names.OpEq, otherValue);
                if (valueEquals.Falsy) return MRubyValue.False;
            }
            else
            {
                return MRubyValue.False;
            }
        }
        return MRubyValue.True;
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod Eql = new((state, self) =>
    {
        var hash = self.As<RHash>();
        var arg = state.GetArg(0);
        if (arg.Object is not RHash other || hash.Length != other.Length)
        {
            return MRubyValue.False;
        }

        if (hash == other)
        {
            return MRubyValue.True;
        }

        foreach (var (key, value) in hash)
        {
            if (other.TryGetValue(key, out var otherValue))
            {
                var valueEquals = state.Send(value, Names.QEql, otherValue);
                if (valueEquals.Falsy) return MRubyValue.False;
            }
            else
            {
                return MRubyValue.False;
            }
        }
        return MRubyValue.True;
    });

    static MRubyValue Default(MRubyState state, RHash hash, MRubyValue key)
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