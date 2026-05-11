using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace MRubyCS;

/// <summary>
/// Default <see cref="IMRubyFiberScheduler"/> implementation backed by .NET
/// thread-pool tasks and timers. Suitable for tests, CLI tools, and any
/// host where the VM is only touched from inside fiber bodies.
/// </summary>
/// <remarks>
/// <para>
/// Each blocking hook spawns work that, when ready, calls
/// <see cref="RFiber.Resume"/> on a thread-pool thread. Hosts that require
/// main-thread affinity (Unity, WPF) must implement their own scheduler
/// that hops back to the VM thread before resuming.
/// </para>
/// <para>
/// Pair with <see cref="RFiber.WaitForTerminateAsync"/> to await a fiber's
/// completion from C#:
/// <code>
/// state.SetFiberScheduler(new ThreadPoolFiberScheduler());
/// var fiber = compiler.LoadSourceCodeAsFiber(code);
/// fiber.Resume();
/// await fiber.WaitForTerminateAsync();
/// </code>
/// </para>
/// </remarks>
public sealed class ThreadPoolFiberScheduler : IMRubyFiberScheduler
{
    readonly ConcurrentDictionary<RFiber, BlockEntry> blockedFibers = new();
    readonly ConcurrentDictionary<RFiber, Timer> sleepTimers = new();
    readonly ConcurrentDictionary<RFiber, TimeoutEntry> timeouts = new();

    // Delegates are cached once per scheduler instance so per-call paths
    // don't allocate a closure or box a ValueTuple<this, fiber> when
    // marshaling state through Timer / CancellationToken.UnsafeRegister /
    // Task.ContinueWith. Per-call state is always a single reference type
    // (typically the RFiber).
    readonly TimerCallback sleepFireCallback;
    readonly Action<object?> sleepCancelCallback;
    readonly Action<Task<MRubyValue>, object?> blockContinuationCallback;
    readonly Action<object?> blockCancelCallback;
    readonly TimerCallback blockTimeoutCallback;
    readonly TimerCallback timeoutFireCallback;
    readonly Action<object?> timeoutCancelCallback;

    sealed class BlockEntry() : TaskCompletionSource<MRubyValue>(TaskCreationOptions.RunContinuationsAsynchronously)
    {
        public Timer? Timer;
    }

    readonly struct TimeoutEntry
    {
        public Timer? Timer { get; init; }
        // Captured for diagnostics / future Fiber#raise wiring; not yet
        // injected into Ruby execution (see TimeoutAfter remarks).
        public RClass? ExceptionClass { get; init; }
    }

    public ThreadPoolFiberScheduler()
    {
        sleepFireCallback = state =>
        {
            var fiber = (RFiber)state!;
            // CAS race guard: whoever removes the entry owns the resume
            // (e.g., concurrent Cancel / TimeoutAfter / sleep-fire).
            if (!sleepTimers.TryRemove(fiber, out var t)) return;
            t.Dispose();
            fiber.Resume();
        };
        sleepCancelCallback = state =>
        {
            var fiber = (RFiber)state!;
            if (sleepTimers.TryRemove(fiber, out var t)) t.Dispose();
        };
        blockTimeoutCallback = state =>
        {
            ((BlockEntry)state!).TrySetCanceled();
        };
        blockCancelCallback = state =>
        {
            ((BlockEntry)state!).TrySetCanceled();
        };
        blockContinuationCallback = (task, state) =>
        {
            var fiber = (RFiber)state!;
            if (!blockedFibers.TryRemove(fiber, out var entry)) return;
            entry.Timer?.Dispose();

            fiber.Resume(task.Status == TaskStatus.RanToCompletion
                ? task.Result
                : MRubyValue.Nil);
        };
        timeoutFireCallback = state =>
        {
            var fiber = (RFiber)state!;
            if (timeouts.TryRemove(fiber, out var te)) te.Timer?.Dispose();

            // Wake the fiber via whichever mechanism it's parked in. Use
            // CAS-style TryRemove so we don't race a concurrent natural
            // wake of the same park into a double Resume.
            if (blockedFibers.TryGetValue(fiber, out var entry))
            {
                // Block's continuation will dispose its timer + Resume.
                entry.TrySetCanceled();
            }
            else if (sleepTimers.TryRemove(fiber, out var st))
            {
                st.Dispose();
                fiber.Resume();
            }
            // Else: fiber wasn't parked in this scheduler's hooks. We
            // can't interrupt arbitrary VM execution from here; drop the
            // deadline silently. (TODO: once a VM-side pending-exception
            // primitive exists, set it on `fiber` so the next yield-point
            // picks it up.)
        };
        timeoutCancelCallback = state =>
        {
            var fiber = (RFiber)state!;
            if (timeouts.TryRemove(fiber, out var entry)) entry.Timer?.Dispose();
        };
    }

    public void KernelSleep(TimeSpan duration, RFiber fiber, CancellationToken cancellationToken = default)
    {
        // TODO: Support TimeProvider
        var timer = new Timer(sleepFireCallback, fiber, duration, Timeout.InfiniteTimeSpan);
        sleepTimers[fiber] = timer;

        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.UnsafeRegister(sleepCancelCallback, fiber);
        }

        // CRuby-style: the hook performs the yield itself. The calling
        // C# primitive (e.g. Kernel#sleep) just calls KernelSleep and
        // returns; this Yield unwinds back to whoever resumed the fiber.
        fiber.Yield();
    }

    public void Block(MRubyValue blocker, RFiber fiber, TimeSpan timeout = default, CancellationToken cancellationToken = default)
    {
        var entry = new BlockEntry();
        blockedFibers[fiber] = entry;

        if (timeout > TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
        {
            entry.Timer = new Timer(blockTimeoutCallback, entry, timeout, Timeout.InfiniteTimeSpan);
        }

        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.UnsafeRegister(blockCancelCallback, entry);
        }

        // Continuation must run regardless of how the task completed
        // (success / cancellation / timeout). Passing cancellationToken to
        // ContinueWith would cancel the continuation itself if the token
        // fires, leaving the fiber permanently blocked. We always run the
        // continuation and dispose the timer there. The resume value
        // (passed to fiber.Resume) is the TCS result on success, or Nil on
        // cancellation/timeout.
        entry.Task.ContinueWith(blockContinuationCallback, fiber, CancellationToken.None,
            TaskContinuationOptions.None, TaskScheduler.Default);

        // CRuby-style: hook yields internally. Callers must have arranged
        // the matching Unblock (or have set a timeout) before getting here.
        fiber.Yield();
    }

    public void Unblock(MRubyValue blocker, RFiber fiber, MRubyValue resumeValue = default)
    {
        if (blockedFibers.TryGetValue(fiber, out var entry))
        {
            entry.TrySetResult(resumeValue);
        }
    }

    public void TimeoutAfter(
        TimeSpan timeout,
        RClass exceptionClass,
        RFiber fiber,
        CancellationToken cancellationToken = default)
    {
        if (timeout <= TimeSpan.Zero || timeout == Timeout.InfiniteTimeSpan) return;

        var entry = new TimeoutEntry
        {
            ExceptionClass = exceptionClass,
            Timer = new Timer(timeoutFireCallback, fiber, timeout, Timeout.InfiniteTimeSpan)
        };
        timeouts[fiber] = entry;

        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.UnsafeRegister(timeoutCancelCallback, fiber);
        }
    }

    public void Dispose()
    {
        foreach (var kv in blockedFibers)
        {
            kv.Value.TrySetCanceled();
        }
        blockedFibers.Clear();

        foreach (var kv in sleepTimers)
        {
            kv.Value.Dispose();
        }
        sleepTimers.Clear();

        foreach (var kv in timeouts)
        {
            kv.Value.Timer?.Dispose();
        }
        timeouts.Clear();
    }
}
