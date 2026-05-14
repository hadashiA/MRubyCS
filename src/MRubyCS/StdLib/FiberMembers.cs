using System;
using MRubyCS.Internals;

namespace MRubyCS.StdLib;

static class FiberMembers
{
    [MRubyMethod(BlockArgument = true)]
    public static MRubyMethod Initialize = new((state, self) =>
    {
        var fiber = self.As<RFiber>();
        var proc = state.GetBlockArgument(false)!;
        fiber.Reset(proc);
        return self;
    });

    [MRubyMethod(RestArguments = true)]
    public static MRubyMethod Resume = new((state, self) =>
    {
        var fiber = self.As<RFiber>();
        var args = state.GetRestArgumentsAfter(0);
        var vmexec = state.Context.CurrentCallInfo.CallerType > CallerType.InVmLoop;
        return fiber.MoveNext(args, false, vmexec);
    });

    [MRubyMethod(RestArguments = true)]
    public static MRubyMethod Transfer = new((state, self) =>
    {
        var fiber = self.As<RFiber>();
        var args = state.GetRestArgumentsAfter(0);
        return fiber.Transfer(args);
    });

    [MRubyMethod]
    public static MRubyMethod Yield = new((state, self) =>
    {
        var fiber = state.Context.Fiber!;
        var args = state.GetRestArgumentsAfter(0);
        fiber.Yield();
        return state.AsFiberResult(args);
    });

    [MRubyMethod]
    public static MRubyMethod Current = new((state, _) => state.CurrentFiber);

    /// <summary>
    /// <c>Fiber.schedule { ... }</c> — convenience for creating a fiber and
    /// starting it immediately under the installed scheduler. Returns the
    /// new <see cref="RFiber"/>; the caller can observe it via
    /// <c>alive?</c> or just rely on the scheduler to drive it to completion.
    /// CRuby's <c>blocking: false</c> distinction is implicit here: any
    /// fiber under a scheduler that hits <c>sleep</c>/<c>Thread.pass</c>/IO
    /// will dispatch through the scheduler.
    /// </summary>
    [MRubyMethod(BlockArgument = true)]
    public static MRubyMethod Schedule = new((state, _) =>
    {
        var block = state.GetBlockArgument(false)!;
        var fiber = state.CreateFiber(block);
        fiber.Resume();
        return new MRubyValue(fiber);
    });

    [MRubyMethod]
    public static MRubyMethod Alive = new((state, self) =>
    {
        var fiber = self.As<RFiber>();
        return fiber.IsAlive;
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod OpEq = new((state, self) =>
    {
        var fiber = self.As<RFiber>();
        var arg = state.GetArgumentAt(0);
        if (arg.Object is RFiber other)
        {
            return fiber == other;
        }
        return MRubyValue.False;
    });

    [MRubyMethod]
    public static MRubyMethod ToS = new((state, self) =>
    {
        var fiber = self.As<RFiber>();
        var result = state.NewString("#<"u8);
        var c = state.ClassOf(self).GetRealClass();
        result.Concat(state.NameOf(c));
        result.Concat(":"u8);

        var s = fiber.State switch
        {
            FiberState.Created => "created"u8,
            FiberState.Running => "running"u8,
            FiberState.Resumed => "resumed"u8,
            FiberState.Suspended => "suspended"u8,
            FiberState.Transferred => "transferred"u8,
            FiberState.Terminated => "terminated"u8,
            _ => throw new ArgumentOutOfRangeException()
        };
        result.Concat("("u8);
        result.Concat(s);
        result.Concat(")"u8);
        return result;
    });
}
