using System;

namespace MRubyCS.StdLib;

static class ClassMembers
{
    [MRubyMethod(OptionalArguments = 1, BlockArgument = true)]
    public static MRubyMethod NewClass = new((state, self) =>
    {
        var superClass = state.TryGetArgumentAt(0, out var superValue)
            ? superValue.As<RClass>()
            : state.ObjectClass;

        var newClass = new RClass(state.ClassClass)
        {
            Super = superClass,
            InstanceVType = superClass.InstanceVType
        };

        superClass.SetFlag(MRubyObjectFlags.ClassInherited);

        var newClassValue = MRubyValue.From(newClass);
        if (state.TryFindMethod(newClass.Class, Names.Initialize, out var method, out _) &&
            method == Initialize)
        {
            Initialize.Invoke(state, newClassValue);
        }
        else
        {
            var block = state.GetBlockArgument();
            state.Send(newClassValue, Names.Initialize,
                [MRubyValue.From(superClass)],
                default,
                block.IsNil ? null : block.As<RProc>());
        }
        state.ClassInheritedHook(superClass, newClass);
        return newClassValue;
    });

    public static MRubyMethod New = new((state, self) =>
    {
        var args = state.GetRestArgumentsAfter(0);
        var kargs = state.GetKeywordArguments();
        var block = state.GetBlockArgument();

        var c = self.As<RClass>();
        if (c.VType == MRubyVType.SClass)
        {
            state.Raise(Names.TypeError, "can't create instance of singleton class"u8);
        }

        var instance = c.InstanceVType switch
        {
            MRubyVType.Array => new RArray(0, c),
            MRubyVType.Hash => new RHash(0, state.HashKeyEqualityComparer, state.ValueEqualityComparer, c),
            MRubyVType.String => new RString(0, c),
            MRubyVType.Range => throw new NotImplementedException(),
            MRubyVType.Exception => new RException(null!, c),
            MRubyVType.Object => new RObject(c.InstanceVType, c),
            MRubyVType.Class => new RClass(c, c.InstanceVType)
            {
                InstanceVType = c.InstanceVType,
                Super = state.ObjectClass
            },
            MRubyVType.Module => new RClass(c, c.InstanceVType)
            {
                InstanceVType = MRubyVType.Undef,
                Super = null!
            },
            _ => throw new InvalidOperationException()
        };
        var instanceValue = MRubyValue.From(instance);
        state.Send(instanceValue, Names.Initialize, args, kargs,
            block.IsNil ? null : block.As<RProc>());
        return instanceValue;
    });

    [MRubyMethod]
    public static MRubyMethod Superclass = new((state, self) =>
    {
        var c = self.As<RClass>().AsOrigin().Super;
        while (c != null! && c.VType == MRubyVType.IClass)
        {
            c = c.AsOrigin().Super;
        }
        return c == null ? MRubyValue.Nil : MRubyValue.From(c);
    });


    [MRubyMethod(OptionalArguments = 1, BlockArgument = true)]
    public static MRubyMethod Initialize = new((state, self) =>
    {
        var c = self.As<RClass>();
        var block = state.GetBlockArgument();
        if (block.Object is RProc proc)
        {
            state.YieldWithClass(c, self, [self], proc);
        }
        return self;
    });
}
