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
        fiber.MoveNext(args, false, vmexec, out var result);
        return result;
    });

    [MRubyMethod(RestArguments = true)]
    public static MRubyMethod Transfer = new((state, self) =>
    {
        var fiber = self.As<RFiber>();
        var args = state.GetRestArgumentsAfter(0);
        fiber.Transfer(args, out var result);
        return result;
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
    public static MRubyMethod Current = new((state, self) =>
    {
        return MRubyValue.From(state.CurrentFiber);
    });

    [MRubyMethod]
    public static MRubyMethod Alive = new((state, self) =>
    {
        var fiber = self.As<RFiber>();
        return MRubyValue.From(fiber.IsAlive);
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod OpEq = new((state, self) =>
    {
        var fiber = self.As<RFiber>();
        var arg = state.GetArgumentAt(0);
        if (arg.Object is RFiber other)
        {
            return MRubyValue.From(fiber == other);
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
        return MRubyValue.From(result);
    });
}
