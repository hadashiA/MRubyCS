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
        //   if (mrb->c->ci->cci > 0) {
        //   vmexec = TRUE;
        // }
        var vmexec = state.Context.CurrentCallInfo.CallerType > CallerType.InVmLoop;

        if (fiber.MoveNext(args, false, vmexec, out var error))
        {
            return state.AsFiberResult(args);
        }
        return MRubyValue.From(error);
    });

    [MRubyMethod()]
    public static MRubyMethod Transfer = new((state, self) =>
    {

    });
}
