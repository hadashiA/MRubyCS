using System;

namespace MRubyCS.StdLib;

static class ModuleMembers
{
    public static MRubyMethod Public = new((mrb, mod) =>
    {
        SetMethodVisibility(mrb, mod.As<RClass>(), MRubyMethodVisibility.Public);
        return mod;
    });

    public static MRubyMethod Private = new((mrb, mod) =>
    {
        SetMethodVisibility(mrb, mod.As<RClass>(), MRubyMethodVisibility.Private);
        return mod;
    });

    public static MRubyMethod Protected = new((mrb, mod) =>
    {
        SetMethodVisibility(mrb, mod.As<RClass>(), MRubyMethodVisibility.Protected);
        return mod;
    });

    public static MRubyMethod TopPublic = new((mrb, mod) =>
    {
        SetMethodVisibility(mrb, mrb.ObjectClass, MRubyMethodVisibility.Public);
        return mrb.ObjectClass;
    });

    public static MRubyMethod TopPrivate = new((mrb, mod) =>
    {
        SetMethodVisibility(mrb, mrb.ObjectClass, MRubyMethodVisibility.Private);
        return mrb.ObjectClass;
    });

    public static MRubyMethod TopProtected = new((mrb, mod) =>
    {
        SetMethodVisibility(mrb, mrb.ObjectClass, MRubyMethodVisibility.Protected);
        return mrb.ObjectClass;
    });

    [MRubyMethod]
    public static MRubyMethod Initialize = new((state, self) =>
    {
        var mod = self.As<RClass>();
        var block = state.GetBlockArgument();
        if (block != null)
        {
            state.YieldWithClass(mod, self, [self], block);
        }
        return self;
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod ExtendObject = new((state, self) =>
    {
        // state.EnsureValueType(self, MRubyVType.Module);
        var obj = state.GetArgumentAt(0);
        var target = state.SingletonClassOf(obj);
        state.IncludeModule(target, self.As<RClass>());
        return self;
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod PrependFeatures = new((state, self) =>
    {
        state.EnsureValueType(self, MRubyVType.Module);
        var c = state.GetArgumentAt(0);
        state.PrependModule(c.As<RClass>(), self.As<RClass>());
        return self;
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod AppendFeatures = new((state, self) =>
    {
        state.EnsureValueType(self, MRubyVType.Module);
        var c = state.GetArgumentAt(0);
        state.IncludeModule(c.As<RClass>(), self.As<RClass>());
        return self;
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod QInclude = new((state, self) =>
    {
        var c = self.As<RClass>();
        var mod2 = state.GetArgumentAt(0);
        state.EnsureValueType(mod2, MRubyVType.Module);

        while (c != null!)
        {
            if (c.VType == MRubyVType.IClass && c.Class == mod2.As<RClass>())
            {
                return MRubyValue.True;
            }

            c = c.Super;
        }

        return MRubyValue.False;
    });

    [MRubyMethod(RestArguments = true)]
    public static MRubyMethod ClassEval = new((state, self) =>
    {
        var block = state.GetBlockArgument(false);
        return state.EvalUnder(self, block!, self.As<RClass>());
    });

    [MRubyMethod(RestArguments = true)]
    public static MRubyMethod ModuleFunction = new((state, self) =>
    {
        state.EnsureValueType(self, MRubyVType.Module);
        var argv = state.GetRestArgumentsAfter(0);
        if (argv.Length <= 0)
        {
            return self;
        }

        var mod = self.As<RClass>();

        foreach (var arg in argv)
        {
            state.EnsureValueType(arg, MRubyVType.Symbol);
            if (!state.TryFindMethod(mod, arg.SymbolValue, out var method, out _))
            {
                state.RaiseNameError(
                    arg.SymbolValue,
                    state.NewString($"undefined method '{state.NameOf(arg.SymbolValue)}' for class {state.NameOf(mod)}"));
            }

            state.DefineClassMethod(mod, arg.SymbolValue, method);
        }
        return self;
    });

    [MRubyMethod(RestArguments = true)]
    public static MRubyMethod AttrReader = new((state, self) =>
    {
        var mod = self.As<RClass>();
        var argv = state.GetRestArgumentsAfter(0);
        foreach (var arg in argv)
        {
            var methodId = state.AsSymbol(arg);
            var name = state.PrepareInstanceVariableName(methodId);

            state.EnsureInstanceVariableName(name);

            state.DefineMethod(mod, methodId, (s, _) =>
            {
                var runtimeSelf = s.GetSelf();
                return state.GetInstanceVariable(runtimeSelf.Object, name);
            });
        }
        return MRubyValue.Nil;
    });

    [MRubyMethod(RestArguments = true)]
    public static MRubyMethod AttrWriter = new((state, self) =>
    {
        var mod = self.As<RClass>();
        var argv = state.GetRestArgumentsAfter(0);
        foreach (var arg in argv)
        {
            var attrId = state.AsSymbol(arg);
            var variableName = state.PrepareInstanceVariableName(attrId);
            var setterName = state.PrepareName(attrId, default, "="u8);

            state.DefineMethod(mod, setterName, (s, _) =>
            {
                var runtimeSelf = s.GetSelf();
                var value = s.GetArgumentAt(0);
                state.SetInstanceVariable(runtimeSelf.Object!, variableName, value);
                return MRubyValue.Nil;
            });
        }
        return MRubyValue.Nil;
    });

    [MRubyMethod(RestArguments = true)]
    public static MRubyMethod AttrAccessor = new((state, mod) =>
    {
        AttrReader.Invoke(state, mod);
        return AttrWriter.Invoke(state, mod);
    });

    [MRubyMethod]
    public static MRubyMethod ToS = new((state, self) =>
    {
        var mod = self.As<RClass>();
        if (mod.VType == MRubyVType.SClass)
        {
            var v = mod.InstanceVariables.Get(Names.AttachedKey);
            return v.VType.IsClass()
                ? state.NewString($"<Class:{state.InspectObject(v)}>")
                : state.NewString($"<Class:{state.StringifyAny(v)}>");
        }

        return state.NameOf(mod);
    });

    [MRubyMethod(RequiredArguments = 2)]
    public static MRubyMethod AliasMethod = new((state, self) =>
    {
        var mod = self.As<RClass>();
        var newName = state.GetArgumentAt(0).SymbolValue;
        var oldName = state.GetArgumentAt(1).SymbolValue;
        state.AliasMethod(mod, newName, oldName);
        state.MethodAddedHook(mod, newName);
        return self;
    });

    [MRubyMethod(RestArguments = true)]
    public static MRubyMethod UndefMethod = new((state, self) =>
    {
        var c = self.As<RClass>();
        var argv = state.GetRestArgumentsAfter(0);
        foreach (var arg in argv)
        {
            state.UndefMethod(c, arg.SymbolValue);
        }

        return self;
    });

    [MRubyMethod]
    public static MRubyMethod Ancestors = new((state, self) =>
    {
        var c = self.As<RClass>();
        var result = state.NewArray();

        while (c != null!)
        {
            if (c.VType == MRubyVType.IClass)
            {
                result.Push(c.Class);
            }
            else if (!c.Flags.HasFlag(MRubyObjectFlags.ClassPrepended))
            {
                result.Push(c);
            }

            c = c.Super;
        }

        return result;
    });

    [MRubyMethod(RequiredArguments = 1, OptionalArguments = 1)]
    public static MRubyMethod ConstDefined = new((state, self) =>
    {
        var mod = self.As<RClass>();
        var id = state.GetArgumentAsSymbolAt(0);
        var inherit = state.GetArgumentAt(1);
        state.EnsureConstName(id);
        var result = inherit.Truthy
            ? state.ConstDefinedAt(id, mod)
            : state.ConstDefinedAt(id, mod, true);
        return result;
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod ConstGet = new((state, self) =>
    {
        if (self.VType is not (MRubyVType.Class or MRubyVType.Module or MRubyVType.SClass))
        {
            state.Raise(Names.TypeError, "constant look-up for non class/module"u8);
        }

        var mod = self.As<RClass>();
        var path = state.GetArgumentAt(0);
        if (path.IsSymbol)
        {
            return state.GetConst(path.SymbolValue, mod);
        }

        // const get with class path string
        state.EnsureValueType(path, MRubyVType.String);
        var pathString = path.As<RString>().AsSpan();
        MRubyValue result;
        while (true)
        {
            var end = pathString.IndexOf("::"u8);
            if (end < 0) end = pathString.Length;
            var id = state.Intern(pathString[..end]);
            result = state.GetConst(id, mod);

            if (end == pathString.Length)
            {
                break;
            }

            mod = result.As<RClass>();
            pathString = pathString[(end + 2)..];
        }

        return result;
    });

    [MRubyMethod(RequiredArguments = 2)]
    public static MRubyMethod ConstSet = new((state, self) =>
    {
        var mod = self.As<RClass>();
        var id = state.GetArgumentAt(0).SymbolValue;
        var value = state.GetArgumentAt(1);
        state.DefineConst(mod, id, value);
        return value;
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod RemoveConst = new((state, self) =>
    {
        var n = state.GetArgumentAt(0).SymbolValue;
        state.EnsureConstName(n);
        var removed = state.RemoveInstanceVariable(self.As<RObject>(), n);
        if (removed.IsUndef)
        {
            state.RaiseNameError(n, state.NewString($"constant {n} is not defined"));
        }

        return removed;
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod ConstMissing = new((state, self) =>
    {
        var name = state.GetArgumentAsSymbolAt(0);
        state.RaiseConstMissing(self.As<RClass>(), name);
        return MRubyValue.Nil;
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod MethodDefined = new((state, self) =>
    {
        var methodId = state.GetArgumentAsSymbolAt(0);
        return state.RespondTo(self.As<RClass>(), methodId);
    });

    [MRubyMethod(RequiredArguments = 1, OptionalArguments = 1, BlockArgument = true)]
    public static MRubyMethod DefineMethod = new((state, self) =>
    {
        var methodId = state.GetArgumentAsSymbolAt(0);
        var proc = state.GetArgumentAt(1);
        var block = state.GetBlockArgument();

        RProc? p;
        if (block != null)
        {
            p = block;
        }
        else
        {
            if (proc is { IsUndef: false, IsProc: false })
            {
                state.Raise(
                    Names.TypeError,
                    $"wrong argument type {state.Stringify(proc)} (expected Proc)");
            }
            p = proc.As<RProc>();
        }

        p = (RProc)p.Clone();
        p.SetFlag(MRubyObjectFlags.ProcStrict);
        var method = new MRubyMethod(p);

        var mod = self.As<RClass>();
        state.DefineMethod(mod, methodId, method);
        state.MethodAddedHook(mod, methodId);

        return methodId;
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod Eqq = new((state, self) =>
    {
        var mod = self.As<RClass>();
        var other = state.GetArgumentAt(0);
        return state.KindOf(other, mod);
    });

    [MRubyMethod]
    public static MRubyMethod Dup = new((state, self) =>
    {
        var clone = state.CloneObject(self);
        if (clone.Object is { } obj)
        {
            obj.UnFreeze();
        }
        return clone;
    });

    static void SetMethodVisibility(MRubyState mrb, RClass c, MRubyMethodVisibility visibility)
    {
        var args = mrb.GetRestArgumentsAfter(0);
        if (args.Length <= 0)
        {
            ref var callInfo = ref mrb.Context.FindClosestVisibilityScope(null, 1, out var env);
            if (env != null)
            {
                env.Visibility = visibility;
            }
            else
            {
                callInfo.Visibility = visibility;
            }
        }
        else
        {
            foreach (var arg in args)
            {
                mrb.EnsureValueType(arg, MRubyVType.Symbol);
                var methodId = arg.SymbolValue;
                c.TryFindMethod(methodId, out var method, out _);
                c.MethodTable[methodId] = method.WithVisibility(visibility);
            }
        }
    }
}
