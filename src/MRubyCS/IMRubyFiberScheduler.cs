using System;
using System.IO;
using System.Threading;

namespace MRubyCS;

/// <summary>
/// Handle returned by <see cref="IMRubyFiberScheduler.Suspend"/>. Holds a
/// reference to its <see cref="IMRubyFiberScheduler"/> and dispatches
/// settle operations through it. Idempotency is delegated to the scheduler
/// (the underlying parking entry is one-shot).
/// </summary>
public readonly struct FiberContinuation
{
    readonly IMRubyFiberScheduler scheduler;

    /// <summary>The fiber this continuation will resume.</summary>
    public RFiber Fiber { get; }

    internal FiberContinuation(IMRubyFiberScheduler scheduler, RFiber fiber)
    {
        this.scheduler = scheduler;
        Fiber = fiber;
    }

    /// <summary>
    /// Resume the suspended fiber with <paramref name="value"/>. The actual
    /// <see cref="RFiber.Resume"/> is deferred to the scheduler's preferred
    /// thread; calling this from inside the parent VM execution is safe.
    /// </summary>
    public void Resume(MRubyValue value = default) => scheduler.ResumeFiber(Fiber, value);

    /// <summary>
    /// Resume the fiber as if cancelled. Delivers <c>nil</c> as the resume
    /// value (matching CRuby fiber-scheduler cancellation). Pass the
    /// originating <paramref name="cancellationToken"/> to preserve it in
    /// the underlying <see cref="OperationCanceledException"/>.
    /// </summary>
    public void SetCancelled(CancellationToken cancellationToken = default)
        => scheduler.CancelFiber(Fiber, cancellationToken);

    /// <summary>
    /// Resume the fiber with an exception. The scheduler wraps
    /// <paramref name="exception"/> as an <see cref="RException"/> and sets
    /// it on <see cref="RFiber.PendingException"/>, so on resume the VM
    /// re-raises it (catchable by Ruby <c>rescue</c>).
    /// </summary>
    public void SetException(Exception exception)
    {
        if (exception is null) throw new ArgumentNullException(nameof(exception));
        scheduler.FailFiber(Fiber, exception);
    }
}

/// <summary>
/// CRuby-style <c>Fiber::Scheduler</c> hook surface. Hooks operate on
/// <see cref="MRubyState.CurrentFiber"/> (captured at hook entry) and return
/// immediately; the scheduler resumes the fiber on the VM thread later.
/// The scheduler receives its <see cref="MRubyState"/> reference via
/// <see cref="Attach"/>, invoked automatically by
/// <see cref="MRubyState.SetFiberScheduler"/>.
/// </summary>
/// <remarks>
/// On exception inside a stream hook, set <see cref="RFiber.PendingException"/>
/// and call <see cref="RFiber.Resume()"/>; the VM re-raises so Ruby
/// <c>rescue</c> can catch it.
/// </remarks>
public interface IMRubyFiberScheduler : IDisposable
{
    /// <summary>
    /// Bind this scheduler to the given <see cref="MRubyState"/>. Invoked
    /// once by <see cref="MRubyState.SetFiberScheduler"/>. A scheduler
    /// instance is intended to serve a single state.
    /// </summary>
    void Attach(MRubyState mrb);

    /// <summary><c>Kernel#sleep</c>: resume after <paramref name="duration"/>.</summary>
    void KernelSleep(TimeSpan duration, CancellationToken cancellationToken = default);

    /// <summary><c>Thread.pass</c>: cooperative yield.</summary>
    void Yield(CancellationToken cancellationToken = default);

    /// <summary>
    /// Park <see cref="MRubyState.CurrentFiber"/> and return a continuation
    /// for the caller to arrange resume from external async work. The fiber
    /// is yielded internally before this method returns — no race window for
    /// "arrange-Resume-before-Suspend". For cancellation, register on the
    /// returned <see cref="FiberContinuation"/> directly
    /// (e.g. <c>ct.Register(continuation.SetCancelled)</c>).
    /// </summary>
    FiberContinuation Suspend();

    /// <summary>
    /// Bounded async read: reads up to <paramref name="maxBytes"/> from
    /// <paramref name="stream"/> and resumes the fiber with a <c>String</c>
    /// of the read bytes (or <c>nil</c> at EOF, matching <c>IO#read(n)</c>).
    /// </summary>
    void ReadStream(
        Stream stream,
        int maxBytes,
        bool disposeStream = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Unbounded async read: reads <paramref name="stream"/> to EOF and
    /// resumes the fiber with a <c>String</c> of the full content
    /// (matching <c>IO#read</c> with no argument).
    /// </summary>
    void ReadStreamToEnd(
        Stream stream,
        bool disposeStream = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Async write of <paramref name="data"/> to <paramref name="stream"/>.
    /// Resumes with bytes-written (<c>Integer</c>, equal to
    /// <c>data.Length</c> on success).
    /// </summary>
    void WriteStream(
        Stream stream,
        ReadOnlyMemory<byte> data,
        bool disposeStream = false,
        CancellationToken cancellationToken = default);

    // ── FiberContinuation dispatch (called via the struct). ─────────────
    // First call wins; subsequent calls are no-op (the underlying parking
    // entry is one-shot).

    void ResumeFiber(RFiber fiber, MRubyValue value);
    void CancelFiber(RFiber fiber, CancellationToken cancellationToken);
    void FailFiber(RFiber fiber, Exception exception);
}
