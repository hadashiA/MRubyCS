using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace MRubyCS;

/// <summary>
/// Handle returned by <see cref="MRubyFiberScheduler.Suspend"/>. Holds a
/// reference to its <see cref="MRubyFiberScheduler"/> and dispatches settle
/// operations through it. Idempotency is delegated to the underlying
/// parking entry (one-shot).
/// </summary>
public readonly struct FiberContinuation
{
    readonly MRubyFiberScheduler scheduler;

    /// <summary>The fiber this continuation will resume.</summary>
    public RFiber Fiber { get; }

    internal FiberContinuation(MRubyFiberScheduler scheduler, RFiber fiber)
    {
        this.scheduler = scheduler;
        Fiber = fiber;
    }

    /// <summary>
    /// Resume the suspended fiber with <paramref name="value"/>. The actual
    /// <see cref="RFiber.Resume"/> is deferred (the parking entry uses
    /// <see cref="TaskCreationOptions.RunContinuationsAsynchronously"/>), so
    /// calling this from inside the VM frame is safe.
    /// </summary>
    public void Resume(MRubyValue value = default) => scheduler.SetResult(Fiber, value);

    /// <summary>
    /// Resume the fiber as if cancelled. Delivers <c>nil</c> as the resume
    /// value (matching CRuby fiber-scheduler cancellation). Pass the
    /// originating <paramref name="cancellationToken"/> to preserve it in
    /// the underlying <see cref="OperationCanceledException"/>.
    /// </summary>
    public void SetCancelled(CancellationToken cancellationToken = default)
        => scheduler.SetCancelled(Fiber, cancellationToken);

    /// <summary>
    /// Resume the fiber with an exception. The scheduler wraps
    /// <paramref name="exception"/> as an <see cref="RException"/> and sets
    /// it on <see cref="RFiber.PendingException"/>, so on resume the VM
    /// re-raises it (catchable by Ruby <c>rescue</c>).
    /// </summary>
    public void SetException(Exception exception)
    {
        if (exception is null) throw new ArgumentNullException(nameof(exception));
        scheduler.SetException(Fiber, exception);
    }
}

/// <summary>
/// Cooperative scheduler that bridges Ruby fibers to C# async code. Single
/// concrete class — hosts may subclass to override <see cref="KernelSleep"/>
/// / <see cref="Yield"/> / <see cref="Suspend"/>, but thread routing is
/// controlled by <see cref="ContinueOnCapturedContext"/> + the ambient
/// <see cref="SynchronizationContext"/>, not by per-scheduler dispatch.
///
/// <para>
/// Hooks operate on <see cref="MRubyState.CurrentFiber"/> (captured at hook
/// entry) and return immediately; the fiber is resumed later via the
/// <see cref="TaskCompletionSource{T}"/> machinery built into
/// <see cref="Suspend"/>.
/// </para>
/// </summary>
public class MRubyFiberScheduler : IDisposable
{
    protected MRubyState MRubyState = default!;
    readonly ConcurrentDictionary<RFiber, BlockEntry> blockedFibers = new();

    sealed class BlockEntry() : TaskCompletionSource<MRubyValue>(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Whether body awaits should continue on the captured
    /// <see cref="SynchronizationContext"/> (<c>true</c>: equivalent to
    /// <c>ConfigureAwait(true)</c>; <c>false</c>: let continuations land on
    /// the threadpool). Forwarded to the body lambda passed to
    /// <see cref="Await"/>; hosts must thread it through every <c>await</c>
    /// in their lambda for the setting to take effect.
    ///
    /// <para>
    /// Default: <c>true</c>. Hosts with a designated VM thread (Unity main
    /// thread, WPF / WinForms UI thread, custom pumped loops) should keep
    /// this and ensure <see cref="SynchronizationContext.Current"/> is set
    /// on the VM thread. Threadpool-friendly hosts (CLI, server backends)
    /// may set this to <c>false</c> for lower overhead.
    /// </para>
    /// </summary>
    public bool ContinueOnCapturedContext { get; set; } = true;

    /// <summary>
    /// Bind this scheduler to the given <see cref="MRubyCS.MRubyState"/>. Invoked
    /// once by <see cref="MRubyState.UseFiberScheduler"/>.
    /// </summary>
    public virtual void Attach(MRubyState mrb) => MRubyState = mrb;

    /// <summary>
    /// Park the current fiber, run <paramref name="body"/> from the caller's
    /// thread (sync prefix runs there), and resume the fiber with body's
    /// result once it completes. The body receives the bound
    /// <see cref="MRubyCS.MRubyState"/> and the current
    /// <see cref="ContinueOnCapturedContext"/> value; pass the latter to
    /// every <c>await</c> in the body via
    /// <c>.ConfigureAwait(continueOnCapturedContext)</c>.
    ///
    /// <para>
    /// On <see cref="OperationCanceledException"/> from body, the fiber
    /// resumes with <c>nil</c> (CRuby fiber-scheduler convention). On any
    /// other exception, the fiber re-raises it as a Ruby exception
    /// (catchable by <c>begin/rescue</c>).
    /// </para>
    /// </summary>
    public void Await(Func<MRubyState, bool, ValueTask<MRubyValue>> body)
    {
        if (body is null) throw new ArgumentNullException(nameof(body));
        var continuation = Suspend();
        _ = AwaitAsync(body, continuation);
        return;

        async ValueTask AwaitAsync(
            Func<MRubyState, bool, ValueTask<MRubyValue>> body,
            FiberContinuation continuation)
        {
            try
            {
                var value = await body(MRubyState, ContinueOnCapturedContext)
                    .ConfigureAwait(ContinueOnCapturedContext);
                continuation.Resume(value);
            }
            catch (OperationCanceledException ex)
            {
                continuation.SetCancelled(ex.CancellationToken);
            }
            catch (Exception ex)
            {
                continuation.SetException(ex);
            }
        }
    }

    /// <summary>
    /// Low-level park primitive. Use when the resume signal arrives from an
    /// arbitrary event source (callbacks, IObservable, custom event loops)
    /// that doesn't fit the <see cref="Await"/> single-task shape.
    ///
    /// <para>
    /// The fiber is yielded internally before this returns — no race window
    /// for "arrange-Resume-before-Suspend". Settlement on the returned
    /// continuation is one-shot.
    /// </para>
    /// </summary>
    public virtual FiberContinuation Suspend()
    {
        var fiber = MRubyState.CurrentFiber;
        var entry = new BlockEntry();
        if (!blockedFibers.TryAdd(fiber, entry))
        {
            ThrowAlreadyParked(fiber, "Suspend");
        }

        _ = WaitAndResumeAsync();
        var continuation = new FiberContinuation(this, fiber);
        fiber.Yield();
        return continuation;

        async ValueTask WaitAndResumeAsync()
        {
            // Force async boundary so the caller-side fiber.Yield() runs
            // before any resume could fire on the VM frame. Dictionary
            // cleanup is done atomically with the settle inside Set*; we
            // don't need to remove here.
            await Task.Yield();

            MRubyValue value;
            try { value = await entry.Task.ConfigureAwait(ContinueOnCapturedContext); }
            catch (OperationCanceledException) { TryResume(fiber, MRubyValue.Nil); return; }
            catch (Exception ex) { TryResumeWithException(fiber, ex); return; }
            TryResume(fiber, value);
        }
    }

    /// <summary><c>Kernel#sleep</c>: resume after <paramref name="duration"/>.</summary>
    /// <remarks>
    /// Default implementation routes through <see cref="Await"/> + <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.
    /// Cancellation resumes the fiber with <c>nil</c>.
    /// </remarks>
    public virtual void KernelSleep(TimeSpan duration, CancellationToken cancellationToken = default)
    {
        Await(async (_, continueOnCapturedContext) =>
        {
            await Task.Delay(duration, cancellationToken).ConfigureAwait(continueOnCapturedContext);
            return MRubyValue.Nil;
        });
    }

    /// <summary><c>Thread.pass</c>: cooperative yield.</summary>
    /// <remarks>
    /// Default implementation routes through <see cref="Await"/> + <see cref="Task.Yield"/>.
    /// Note that <see cref="Task.Yield"/> has no <c>ConfigureAwait</c> control — it always
    /// captures <see cref="SynchronizationContext.Current"/>; this is expected for a yield
    /// primitive that exists to relinquish the current step to other work.
    /// </remarks>
    public virtual void Yield(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return;
        Await(async (_, _) =>
        {
            await Task.Yield();
            return MRubyValue.Nil;
        });
    }

    // ── FiberContinuation dispatch (called via the struct). ─────────────
    // First call wins; subsequent calls are no-op (the underlying parking
    // entry is one-shot via TaskCompletionSource.TrySet*). Removing from
    // blockedFibers atomically with the successful settle releases the
    // park slot before TryResume runs the fiber, so the fiber can re-park
    // (next `sleep`, next `Suspend`) without hitting "already parked".

    internal void SetResult(RFiber fiber, MRubyValue value)
    {
        if (blockedFibers.TryGetValue(fiber, out var entry) && entry.TrySetResult(value))
            blockedFibers.TryRemove(fiber, out _);
    }

    internal void SetCancelled(RFiber fiber, CancellationToken cancellationToken)
    {
        if (blockedFibers.TryGetValue(fiber, out var entry) && entry.TrySetCanceled(cancellationToken))
            blockedFibers.TryRemove(fiber, out _);
    }

    internal void SetException(RFiber fiber, Exception exception)
    {
        if (blockedFibers.TryGetValue(fiber, out var entry) && entry.TrySetException(exception))
            blockedFibers.TryRemove(fiber, out _);
    }

    static void TryResume(RFiber fiber, MRubyValue value)
    {
        if (!fiber.IsAlive) return;
        // Exceptions are routed via resumeSource for WaitForTerminate observers;
        // swallow here so threadpool / context callbacks don't crash the process.
        try { fiber.Resume(value); }
        catch { }
    }

    static void TryResumeWithException(RFiber fiber, Exception ex)
    {
        if (!fiber.IsAlive) return;
        try
        {
            var mrb = fiber.MRubyState;
            var msg = ex.Message ?? ex.GetType().Name;
            fiber.PendingException = new RException(
                mrb.NewString(System.Text.Encoding.UTF8.GetBytes(msg)),
                mrb.GetExceptionClass(Names.RuntimeError));
            fiber.Resume();
        }
        catch { }
    }

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    static void ThrowAlreadyParked(RFiber fiber, string op) =>
        throw new InvalidOperationException(
            $"{op}: fiber is already parked under this scheduler. Each park must be matched by Resume/SetCancelled/SetException/cancel before another can be issued.");

    public virtual void Dispose()
    {
        foreach (var kv in blockedFibers) kv.Value.TrySetCanceled();
        blockedFibers.Clear();
    }
}
