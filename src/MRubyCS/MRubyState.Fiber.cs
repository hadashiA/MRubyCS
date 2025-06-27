using System;
using MRubyCS.Internals;

namespace MRubyCS;

partial class MRubyState
{
    public RFiber CurrentFiber => Context.Fiber ??= new RFiber(Context, this, FiberClass);

    public RFiber CreateFiber(RProc proc)
    {
        var fiber = new RFiber(this, FiberClass);
        fiber.Reset(proc);
        return fiber;
    }

    public RFiber CreateFiber(Irep irep)
    {
        var proc = new RProc(irep, 0, ProcClass)
        {
            Upper = null,
            Scope = ObjectClass
        };
        return CreateFiber(proc);
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

    internal void SwitchToContext(MRubyContext newContext)
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
        for (var i = context.CallDepth; i >= 0; i--)
        {
            if (context.CallStack[i].CallerType > CallerType.InVmLoop)
            {
                Raise(Names.FiberError, "can't cross C# function boundary"u8);
            }
        }
    }

    internal void EnsureValidFiberBoundaryRecursive(MRubyContext context)
    {
        var c = context.Previous;
        while (c != null)
        {
            EnsureValidFiberBoundary(c);
            if (c == ContextRoot) break;
            c = c.Previous;
        }
    }
}
