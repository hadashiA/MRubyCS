namespace MRubyCS.StdLib;

/// <summary>
/// MRubyCS does not implement OS threads (the VM is single-threaded);
/// the <c>Thread</c> class exists primarily to host CRuby-compatible
/// cooperative-scheduling entry points such as <c>Thread.pass</c>.
/// </summary>
static class ThreadMembers
{
    /// <summary>
    /// <c>Thread.pass</c> — CRuby-compatible cooperative yield. Hands
    /// control back to the <see cref="IMRubyFiberScheduler"/> so other
    /// in-flight fibers and host async work can run before this fiber is
    /// resumed. No-op at the root fiber or when no scheduler is installed.
    /// </summary>
    [MRubyMethod]
    public static MRubyMethod Pass = new((state, _) =>
    {
        var fiber = state.CurrentFiber;
        var scheduler = state.FiberScheduler;
        if (scheduler is not null && !fiber.IsRoot)
        {
            scheduler.Yield(fiber);
        }
        return MRubyValue.Nil;
    });
}
