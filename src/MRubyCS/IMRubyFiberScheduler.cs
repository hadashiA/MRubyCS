using System;
using System.Threading;
using System.Threading.Tasks;

namespace MRubyCS;

/// <summary>
/// CRuby-style Fiber scheduler hook surface, adapted for C# integration.
/// Hosts implement this to make blocking Ruby primitives (currently
/// <c>Kernel#sleep</c>, <c>Fiber.pass</c>, I/O via <see cref="Await{T}"/>)
/// cooperate with an async runtime such as UniTask.
/// </summary>
/// <remarks>
/// <para>
/// Hooks are <b>synchronous from MRubyCS's perspective</b>: each one records
/// the request and returns immediately. The scheduler is responsible for
/// arranging that the fiber is later resumed via <see cref="RFiber.Resume"/>
/// on the host's VM thread. The dispatch sites in MRubyCS guard against
/// invoking these from the root fiber.
/// </para>
/// <para>
/// Thread affinity is the host's responsibility. The scheduler decides
/// where continuations run; with <see cref="ThreadPoolFiberScheduler"/>
/// they fire on threadpool threads, with a UniTask-backed scheduler they
/// would fire on Unity's PlayerLoop main thread.
/// </para>
/// <para>
/// <b>Exception delivery</b>: if a <see cref="Await"/> task throws,
/// implementations should wrap the exception as an <see cref="RException"/>
/// and set <see cref="RFiber.PendingException"/> before resuming the fiber.
/// MRubyCS detects the pending exception on resume and re-raises it inside
/// the fiber's VM execution, allowing Ruby <c>rescue</c> to catch it.
/// </para>
/// </remarks>
public interface IMRubyFiberScheduler : IDisposable
{
    /// <summary>
    /// Called from <c>Kernel#sleep(d)</c> with <c>d &gt; 0</c> on a non-root
    /// fiber. The implementation must arrange for <c>fiber.Resume()</c>
    /// after <paramref name="duration"/> elapses (or
    /// <paramref name="cancellationToken"/> fires).
    /// </summary>
    void KernelSleep(TimeSpan duration, RFiber fiber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Park <paramref name="fiber"/> until <see cref="Unblock"/> is called
    /// with the same <paramref name="blocker"/>, or until
    /// <paramref name="cancellationToken"/> fires.
    /// </summary>
    /// <remarks>
    /// Callers must arrange the eventual <see cref="Unblock"/> (e.g. queue
    /// a <c>Task</c>) <b>before</b> invoking <c>Block</c>; otherwise the
    /// caller and the unblock plumbing race for the recorded-park state.
    /// Apply a deadline by combining the caller's
    /// <see cref="CancellationToken"/> with one that auto-cancels (e.g.
    /// <see cref="CancellationTokenSource.CancelAfter(TimeSpan)"/>).
    /// </remarks>
    void Block(MRubyValue blocker, RFiber fiber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Wake a fiber currently parked in <see cref="Block"/>.
    /// <paramref name="resumeValue"/> becomes the return value of the
    /// C#-side primitive that called <see cref="Block"/>.
    /// May be called from any thread.
    /// </summary>
    void Unblock(MRubyValue blocker, RFiber fiber, MRubyValue resumeValue = default);

    /// <summary>
    /// Cooperative yield (<c>Thread.pass</c> equivalent): park the current
    /// fiber and arrange for it to be resumed at the next available
    /// opportunity, letting other in-flight fibers and host work run.
    /// </summary>
    void Yield(RFiber fiber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Await a fire-and-forget task on the scheduler's preferred thread.
    /// When complete, resume <paramref name="fiber"/> with
    /// <paramref name="resumeValue"/>. On exception, deliver via
    /// <see cref="RFiber.PendingException"/>.
    /// </summary>
    void Await(
        ValueTask task,
        RFiber fiber,
        MRubyValue resumeValue = default,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Await a value-returning task on the scheduler's preferred thread,
    /// project the result on the VM thread, and resume the fiber with it.
    /// </summary>
    void Await<T>(
        ValueTask<T> task,
        RFiber fiber,
        Func<T, MRubyValue> mapResult,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Hot-path overload of <see cref="Await{T}"/>: passes
    /// <paramref name="state"/> alongside the result so callers can supply
    /// a static (non-capturing) <paramref name="mapResult"/> delegate.
    /// </summary>
    void Await<T, TState>(
        ValueTask<T> task,
        RFiber fiber,
        Func<T, TState, MRubyValue> mapResult,
        TState state,
        CancellationToken cancellationToken = default);
}
