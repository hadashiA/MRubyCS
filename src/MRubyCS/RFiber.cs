using System;
using MRubyCS.Internals;

namespace MRubyCS;

public sealed class RFiber : RObject
{
    public RProc? Proc { get; private set; }
    public FiberState State => context.State;
    public bool IsAlive => context.State != FiberState.Terminated;

    readonly MRubyContext context = new();
    readonly MRubyState state;

    internal RFiber(MRubyState state, RClass c) : base(MRubyVType.Fiber, c)
    {
        context.Fiber = this;
        this.state = state;
    }

    public void Reset(RProc proc)
    {
        Proc = proc;

        context.UnwindStack();

        // copy receiver from a block
        context.Stack[0] = state.Context.CurrentStack[0];

        // adjust return callinfo
        ref var callInfo = ref context.CurrentCallInfo;
        callInfo.Scope = proc.Scope!;
        callInfo.Proc = proc;

        // push dummy callinfo
        context.CallStack[1] = state.Context.CallStack[0];
        context.PushCallStack();
    }

    public bool Resume(ReadOnlySpan<MRubyValue> args, out RException? error)
    {
        return MoveNext(args, false, true, out error);
    }

    public bool Transfer(ReadOnlySpan<MRubyValue> args, out RException? error)
    {
        state.EnsureValidFiberBoundaryRecursive();
        if (context.State == FiberState.Resumed)
        {
            error = FiberError("attempt to transfer to a resuming fiber"u8);
            return false;
        }

        if (context == state.ContextRoot)
        {
            state.Context.State = FiberState.Transferred;
            state.SwitchContextTo(context);
            context.State = FiberState.Running;
            context.CurrentCallInfo.Scope = null!;

            error = default!;
            return true;
        }

        if (context == state.Context)
        {
            error = default!;
            return true;
        }

        return MoveNext(args, true, false, out error);
    }

    public void Yield()
    {
        if (context.Previous is null)
        {
            state.Raise(Names.FiberError, "attempt to yield the current fiber"u8);
        }

        if (context == state.ContextRoot)
        {
            state.Raise(Names.FiberError, "can't yield from root fiber"u8);
        }

        if (context.Previous?.State == FiberState.Transferred)
        {
            state.Raise(Names.FiberError, "attempt to yield on a not resumed fiber"u8);
        }

        state.EnsureValidFiberBoundary(context);
        context.State = FiberState.Suspended;

        context.Previous!.State = FiberState.Running;
        state.SwitchContextTo(context.Previous);
        context.Previous = null;
    }

    internal bool MoveNext(
        ReadOnlySpan<MRubyValue> args,
        bool transfer,
        bool vmexec,
        out RException error)
    {
        if (!transfer && context == state.Context)
        {
            error = FiberError("attempt to resume the current fiber"u8);
            return false;
        }

        var currentStatus = context.State;
        switch (currentStatus)
        {
            case FiberState.Transferred:
                error = FiberError("resuming transferred fiber"u8);
                return false;
            case FiberState.Running:
            case FiberState.Resumed:
                error = FiberError("double resume"u8);
                return false;
            case FiberState.Terminated:
                error = FiberError("resuming dead fiber"u8);
                return false;
        }

        state.EnsureValidFiberBoundary(context);

        var currentContext = state.Context;

        if (transfer)
        {
            currentContext.State = FiberState.Transferred;
            context.Previous = null;
        }
        else
        {
            currentContext.State = FiberState.Resumed;
            context.Previous = currentContext;
        }

        context.Previous = currentContext;
        state.SwitchContextTo(context);

        if (currentStatus == FiberState.Created)
        {
            if (context.CurrentCallInfo.Proc == null)
            {
                error = FiberError("double resume (current)"u8);
                return false;
            }

            if (vmexec)
            {
                context.PopCallStack(); // pop dummy callinfo
            }

            // copy arguments
            if (args.Length >= MRubyCallInfo.CallMaxArgs)
            {
                // pack
                context.ExtendStack(3); // for receiver, args and (optional) block
                context.CallStack[0].ArgumentCount = MRubyCallInfo.CallMaxArgs;
            }
            else
            {
                context.ExtendStack(args.Length + 2); // for receiver and (optional) block
                args.CopyTo(context.Stack.AsSpan(1));
            }

            MRubyValue result;
            if (context.CallStack[0].Scope is REnv env)
            {
                result = env.Stack[0];
            }
            else
            {
                result = MRubyValue.From(state.TopSelf);
            }

            context.Stack[0] = result;
        }
        else
        {
            if (vmexec)
            {
                context.PopCallStack(); // pop dummy callinfo
                context.Stack[0] = state.AsFiberResult(args);
            }
        }

        if (vmexec)
        {
            var currentCallerType = currentContext.CurrentCallInfo.CallerType;
            context.VmExecutedByFiber = true;

            state.Exec(Proc!.Irep);
            state.SwitchContextTo(currentContext);
            // restore values as they may have changed in Fiber.yield
            currentContext.CurrentCallInfo.CallerType = currentCallerType;
        }
        else
        {
            context.CallStack[0].Scope = null!;
        }

        error = default!;
        return true;
    }

    internal void Terminate(ref MRubyCallInfo callInfo)
    {

    }


RException FiberError(ReadOnlySpan<byte> message)
    {
        return new RException(
            state.NewString(message),
            state.GetExceptionClass(Names.FiberError));
    }
}
