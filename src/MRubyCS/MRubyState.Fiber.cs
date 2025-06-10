using System;
using MRubyCS.Internals;

namespace MRubyCS;

partial class MRubyState
{
    public RFiber CreateFiber(ReadOnlySpan<byte> bytecode)
    {
        var proc  = CreateProc(bytecode);
        return CreateFiber(proc);
    }

    public RFiber CreateFiber(RProc proc)
    {
        var fiber = new RFiber(this, FiberClass);
        fiber.Reset(proc);
        return fiber;
    }

    internal MRubyValue AsFiberResult(ReadOnlySpan<MRubyValue> args)
    {
        return args.Length switch
        {
            0 => MRubyValue.Nil,
            1 => args[0],
            _ => MRubyValue.From(NewArray(args))
        };
    }

    internal void SwitchContextTo(MRubyContext newContext)
    {
        Context = newContext;
    }

    internal void EnsureFiberInitialized(RFiber fiber)
    {
        if (fiber.Proc is null)
        {
            Raise(Names.FiberError, "unitialized fiber"u8);
        }
    }

    internal void EnsureValidFiberBoundary(MRubyContext context)
    {
        for (var i = context.CallDepth - 1; i >= 0; i--)
        {
            if (Context.CallStack[i].CallerType > CallerType.InVmLoop)
            {
                Raise(Names.FiberError, "can't cross C# function boundary"u8);
            }
        }
    }

    internal void EnsureValidFiberBoundaryRecursive()
    {
        var context = Context.Previous;
        while (context != null)
        {
            EnsureValidFiberBoundary(context);
            if (context == ContextRoot) break;
            context = Context.Previous;
        }
    }
}