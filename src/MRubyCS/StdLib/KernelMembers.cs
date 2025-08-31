using System;

namespace MRubyCS.StdLib;

static class KernelMembers
{
    public static MRubyMethod InternalCaseEqq = new((state, self) =>
    {
        if (self.IsNil)
        {
            return MRubyValue.False;
        }

        var other = state.GetArgumentAt(0);
        RArray? array = null;
        if (self.Object is RArray x)
        {
            array = x;
        }
        else if (state.RespondTo(self, Names.ToA))
        {
            var arrayValue = state.Send(self, Names.ToA);
            if (!arrayValue.IsNil)
            {
                state.EnsureValueType(arrayValue, MRubyVType.Array);
                array = arrayValue.As<RArray>();
            }
        }
        if (array is null)
        {
            return state.Send(self, Names.OpEqq, other);
        }

        for (var i = 0; i < array.Length; i++)
        {
            var c = state.Send(array[i], Names.OpEqq, other);
            if (c.Truthy)
            {
                return MRubyValue.True;
            }
        }
        return MRubyValue.False;
    });

    [MRubyMethod]
    public static MRubyMethod InternalToInt = new((state, self) =>
    {
        return state.ToInteger(self);
    });

    public static MRubyMethod BlockGiven = new((state, self) =>
    {
        throw new NotSupportedException();
    });

    [MRubyMethod(OptionalArguments = 2)]
    public static MRubyMethod Raise = new((state, self) =>
    {
        var argc = state.GetArgumentCount();
        switch (argc)
        {
            case 0:
                state.Raise(Names.RuntimeError, []);
                break;
            case 1:
                var arg = state.GetArgumentAt(0);
                switch (arg.VType)
                {
                    case MRubyVType.String:
                        state.Raise(Names.RuntimeError, arg.As<RString>());
                        break;
                    case MRubyVType.Exception:
                    {
                        state.Raise(arg.As<RException>());
                        break;
                    }
                    case MRubyVType.Class:
                    {
                        var ex = new RException(state.NewString(""u8), arg.As<RClass>());
                        state.Raise(ex);
                        break;
                    }
                    default:
                        state.Raise(Names.TypeError, $"exception class/object expected");
                        break;
                }
                break;
            case 2:
                var exceptionClass = state.GetArgumentAsClassAt(0);
                var message = state.GetArgumentAsStringAt(1);
                state.Raise(exceptionClass, message);
                break;
        }
        return MRubyValue.Nil; // not reached
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod OpEqq = new((state, self) =>
    {
        var arg = state.GetArgumentAt(0);
        return state.ValueEquals(self, arg);
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod Cmp = new((state, self) =>
    {
        var other = state.GetArgumentAt(0);
        if (state.IsRecursiveCalling(Names.OpCmp, self, other))
        {
            return MRubyValue.Nil;
        }
        if (self == other)
        {
            return 0;
        }
        return MRubyValue.Nil;
    });

    public static MRubyMethod Class = new((state, self) =>
    {
        return state.ClassOf(self).GetRealClass();
    });

    public static MRubyMethod Clone = new((state, self) =>
    {
        return state.CloneObject(self);
    });

    public static MRubyMethod Dup = new((state, self) =>
    {
        return state.DupObject(self);
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod Eql = new((state, self) =>
    {
        return self == state.GetArgumentAt(0);
    });

    public static MRubyMethod Freeze = new((state, self) =>
    {
        if (self.Object is { } obj)
        {
            if (!obj.IsFrozen)
            {
                obj.MarkAsFrozen();
                if (obj.Class.VType == MRubyVType.SClass)
                {
                    obj.Class.MarkAsFrozen();
                }
            }
        }
        return self;
    });

    public static MRubyMethod Frozen = new((state, self) =>
    {
        if (self.Object is { } obj)
        {
            return obj.IsFrozen;
        }
        return MRubyValue.True;
    });

    public static MRubyMethod Hash = new((state, self) =>
    {
        return self.ObjectId;
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod InitializeCopy = new((state, self) =>
    {
        var original = state.GetArgumentAt(0);
        if (original == self) return self;
        if (self.VType != original.VType ||
            state.ClassOf(self) != state.ClassOf(original))
        {
            state.Raise(Names.TypeError, "initialize_copy shoud take same class object"u8);
        }
        return self;
    });

    public static MRubyMethod Inspect = new((state, self) =>
    {
        return state.InspectObject(self);
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod InstanceOf = new((state, self) =>
    {
        var c= state.GetArgumentAsClassAt(0);
        return state.InstanceOf(self, c);
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod KindOf = new((state, self) =>
    {
        var c= state.GetArgumentAsClassAt(0);
        return state.KindOf(self, c);
    });

    public static MRubyMethod ObjectId = new((state, self) =>
    {
        return self.ObjectId;
    });

    [MRubyMethod(RestArguments = true)]
    public static MRubyMethod Print = new((state, self) =>
    {
        var args = state.GetRestArgumentsAfter(0);
        foreach (var arg in args)
        {
            var s = state.Stringify(arg);
            Console.WriteLine(System.Text.Encoding.UTF8.GetString(s.AsSpan()));
        }
        return MRubyValue.Nil;
    });

    [MRubyMethod(RestArguments = true)]
    public static MRubyMethod P = new((state, self) =>
    {
        var args = state.GetRestArgumentsAfter(0);
        foreach (var arg in args)
        {
            var s = state.InspectObject(arg);
            Console.WriteLine(System.Text.Encoding.UTF8.GetString(s.AsSpan()));
        }

        if (args.Length == 1)
        {
            return args[0];
        }
        return state.NewArray(args);
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod RemoveInstanceVariable = new((state, self) =>
    {
        var name = state.GetArgumentAsSymbolAt(0);
        if (self.Object is RObject obj)
        {
            if (obj.InstanceVariables.Remove(name, out var v))
            {
                return v;
            }
        }
        return MRubyValue.Undef;
    });

    [MRubyMethod(RequiredArguments = 1, OptionalArguments = 1)]
    public static MRubyMethod RespondTo = new((state, self) =>
    {
        var methodId = state.GetArgumentAsSymbolAt(0);
        var includesPrivate = state.GetArgumentAt(1).Truthy;
        var result = state.RespondTo(self, methodId);
        if (!result)
        {
            if (state.RespondTo(state.ClassOf(self), methodId))
            {
                return state.Send(self, methodId, methodId, includesPrivate);
            }
        }
        return result;
    });

    public static MRubyMethod ToS = new((state, self) =>
    {
        return state.StringifyAny(self);
    });

    public static MRubyMethod Lambda = new((state, self) =>
    {
        var block = state.GetBlockArgument();
        if (block == null)
        {
            state.Raise(Names.ArgumentError, "tried to create Proc object without a block"u8);
        }

        if (!block!.HasFlag(MRubyObjectFlags.ProcStrict))
        {
            var dup = block.Dup();
            dup.SetFlag(MRubyObjectFlags.ProcStrict);
            return dup;
        }
        return block;
    });
}
