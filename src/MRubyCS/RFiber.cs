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

    internal RFiber(MRubyContext context, MRubyState state, RClass c) : base(MRubyVType.Fiber, c)
    {
        this.context = context;
        this.context.Fiber = this;
        this.state = state;
    }

    public bool Resume(ReadOnlySpan<MRubyValue> args, out MRubyValue result)
    {
        return MoveNext(args, false, true, out result);
    }

    public bool Transfer(ReadOnlySpan<MRubyValue> args, out MRubyValue result)
    {
        state.EnsureValidFiberBoundaryRecursive();
        if (context.State == FiberState.Resumed)
        {
            state.Raise(Names.FiberError, "attempt to transfer to a resuming fiber"u8);
        }

        if (context == state.ContextRoot)
        {
            state.Context.State = FiberState.Transferred;
            state.SwitchToContext(context);
            context.State = FiberState.Running;
            context.CurrentCallInfo.MarkContextModify();
            result = state.AsFiberResult(args);
            return true;
        }

        if (context == state.Context)
        {
            result = state.AsFiberResult(args);
            return true;
        }
        return MoveNext(args, true, false, out result);
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
        state.SwitchToContext(context.Previous);
        context.Previous = null;

        ref var currentCallInfo = ref state.Context.CurrentCallInfo;
        if (context.VmExecutedByFiber)
        {
            context.VmExecutedByFiber = false;
            currentCallInfo.CallerType = CallerType.Resumed;
        }
        currentCallInfo.MarkContextModify();
    }

    internal void Reset(RProc proc)
    {
        Proc = proc;

        context.UnwindStack();

        // copy receiver from a block
        context.Stack[0] = state.Context.Stack[state.Context.CurrentCallInfo.StackPointer];

        // adjust return callinfo
        ref var callInfo = ref context.CurrentCallInfo;
        callInfo.Scope = proc.Scope!.TargetClass;
        callInfo.Proc = proc;

        // push dummy callinfo
        context.CallStack[1] = context.CallStack[0];
        context.PushCallStack();
    }

    internal void Terminate(ref MRubyCallInfo callInfo)
    {
        callInfo.MarkContextModify();
        context.UnwindStack();
        context.State = FiberState.Terminated;
        context.CallStack.AsSpan().Clear();
        context.Stack.AsSpan().Clear();

        state.SwitchToContext(context.Previous ?? state.ContextRoot);
        context.Previous = null;
        state.Context.State = FiberState.Running;
    }

    internal bool MoveNext(ReadOnlySpan<MRubyValue> args, bool transfer, bool vmexec, out MRubyValue result)
    {
        if (!transfer && context == state.Context)
        {
            state.Raise(Names.FiberError, "attempt to transfer to a resuming fiber"u8);
        }

        var currentStatus = context.State;
        switch (currentStatus)
        {
            case FiberState.Transferred:
                state.Raise(Names.FiberError, "resuming transferred fiber"u8);
                break;
            case FiberState.Running:
            case FiberState.Resumed:
                state.Raise(Names.FiberError, "double resume"u8);
                break;
            case FiberState.Terminated:
                state.Raise(Names.FiberError, "resuming dead fiber"u8);
                break;
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

        state.SwitchToContext(context);

        if (currentStatus == FiberState.Created)
        {
            if (context.CurrentCallInfo.Proc == null)
            {
                state.Raise(Names.FiberError, "double resume (current)"u8);
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

            if (Proc!.Scope is REnv env)
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
            result = state.AsFiberResult(args);
            if (vmexec)
            {
                if (context.CallDepth > 0) context.PopCallStack(); // pop dummy callinfo
                context.Stack[context.CallStack[1].StackPointer] = result;
            }
        }

        if (vmexec)
        {
            var currentCallerType = currentContext.CurrentCallInfo.CallerType;
            context.VmExecutedByFiber = true;

            result = state.Exec(Proc!.Irep);
            state.SwitchToContext(currentContext);
            // restore values as they may have changed in Fiber.yield
            currentContext.CurrentCallInfo.CallerType = currentCallerType;
        }
        else
        {
            context.CurrentCallInfo.MarkContextModify();
        }

        return IsAlive;
    }
}