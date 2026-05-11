using System;
using System.Threading;

namespace MRubyCS;

/// <summary>
/// CRuby-style Fiber scheduler hook surface.
/// Hosts implement this to make blocking Ruby primitives (currently
/// <c>Kernel#sleep</c>; later <c>Mutex</c>/<c>Queue</c>/<c>IO</c>) cooperate
/// with an async runtime such as UniTask.
/// </summary>
/// <remarks>
/// <para>
/// Parking hooks (<see cref="KernelSleep"/>, <see cref="Block"/>) follow
/// CRuby's <c>Fiber::Scheduler</c> convention: they record the park, call
/// <see cref="RFiber.Yield"/> internally, and "return" to the caller only
/// when the fiber is later resumed (the value passed to
/// <see cref="RFiber.Resume"/> is delivered into the Ruby caller's stack
/// slot, not through the C# return). Implementations must not invoke
/// blocking hooks from the root fiber — the dispatch sites in MRubyCS
/// (e.g. <c>Kernel#sleep</c>) already guard against this.
/// </para>
/// <para>
/// Thread affinity is the host's responsibility. MRubyCS does not capture a
/// <c>SynchronizationContext</c>. The scheduler must arrange for
/// <c>fiber.Resume()</c> to run on the same thread the VM was last entered
/// on. With UniTask, dispatch via <c>PlayerLoopTiming.Update</c> satisfies
/// this for free.
/// </para>
/// <para>
/// Re-entrancy: scheduler hooks must not call back into Ruby code
/// (e.g. <see cref="MRubyState.Send"/>). <see cref="RFiber.Yield"/> is not
/// re-entrancy — it unwinds the VM rather than invoking Ruby — and is the
/// one expected exception.
/// </para>
/// </remarks>
public interface IMRubyFiberScheduler : IDisposable
{
    /// <summary>
    /// Called from <c>Kernel#sleep</c> on a non-blocking fiber. The
    /// implementation must record the deadline, call
    /// <see cref="RFiber.Yield"/>, and arrange for <c>fiber.Resume()</c>
    /// once the duration elapses (or <paramref name="cancellationToken"/>
    /// fires).
    /// </summary>
    void KernelSleep(TimeSpan duration, RFiber fiber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Park <paramref name="fiber"/> until <see cref="Unblock"/> is called
    /// with the same <paramref name="blocker"/>, or until the optional
    /// timeout elapses. Used as the foundation for <c>Mutex#lock</c>,
    /// <c>Queue#pop</c>, <c>Fiber#join</c> once those classes exist.
    /// The implementation records the park then calls
    /// <see cref="RFiber.Yield"/>. The matching <see cref="Unblock"/>
    /// (or timeout) resumes the fiber with the unblock value, which is
    /// delivered into Ruby as the apparent return value of the C# method
    /// that called <c>Block</c>.
    /// </summary>
    /// <remarks>
    /// Callers must arrange the eventual <see cref="Unblock"/> (e.g. queue
    /// a <c>Task</c>) <b>before</b> invoking <c>Block</c>, otherwise the
    /// caller and the unblock plumbing race for the recorded-park state.
    /// </remarks>
    void Block(
        MRubyValue blocker,
        RFiber fiber,
        TimeSpan timeout = default,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Wake a fiber currently parked in <see cref="Block"/> with the given
    /// <paramref name="blocker"/>. May be called from any thread; the
    /// scheduler is responsible for hopping to the VM thread before
    /// resuming. <paramref name="resumeValue"/> is delivered as the
    /// return value of the <c>Fiber.yield</c> (or equivalent) call that
    /// suspended the fiber — i.e., it becomes the result of the C#-side
    /// blocking primitive that called <see cref="Block"/>.
    /// </summary>
    void Unblock(MRubyValue blocker, RFiber fiber, MRubyValue resumeValue = default);

    /// <summary>
    /// Mark the current fiber as needing a deadline. After
    /// <paramref name="seconds"/> the scheduler should resume the fiber by
    /// raising an instance of <paramref name="exceptionClass"/>.
    /// </summary>
    void TimeoutAfter(
        TimeSpan timeout,
        RClass exceptionClass,
        RFiber fiber,
        CancellationToken cancellationToken = default);
}
