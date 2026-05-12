using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace MRubyCS;

public sealed class ThreadPoolFiberScheduler : IMRubyFiberScheduler
{
    public static readonly ThreadPoolFiberScheduler Instance = new();

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

        entry.Task.ContinueWith(blockContinuationCallback, fiber, CancellationToken.None,
            TaskContinuationOptions.None, TaskScheduler.Default);

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
