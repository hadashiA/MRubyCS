using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace MRubyCS;

/// <summary>
/// Default <see cref="IMRubyFiberScheduler"/> implementation backed by .NET
/// thread-pool tasks and timers. Suitable for tests, CLI tools, and any
/// host where the VM is only touched from inside fiber bodies. Resume
/// callbacks fire on threadpool threads.
/// </summary>
/// <remarks>
/// Hosts requiring main-thread affinity (Unity, WPF) implement their own
/// <see cref="IMRubyFiberScheduler"/> that hops back to the VM thread
/// before calling <see cref="RFiber.Resume"/>.
/// </remarks>
public sealed class ThreadPoolFiberScheduler : IMRubyFiberScheduler
{
    readonly ConcurrentDictionary<RFiber, BlockEntry> blockedFibers = new();
    readonly ConcurrentDictionary<RFiber, Timer> sleepTimers = new();

    // Cached delegates: capture `this` once, never box ValueTuple<this,...>
    // when marshaling state through Timer / UnsafeRegister / ContinueWith.
    readonly TimerCallback sleepFireCallback;
    readonly Action<object?> sleepCancelCallback;
    readonly Action<Task<MRubyValue>, object?> blockContinuationCallback;
    readonly Action<object?> blockCancelCallback;

    sealed class BlockEntry() : TaskCompletionSource<MRubyValue>(TaskCreationOptions.RunContinuationsAsynchronously);

    public ThreadPoolFiberScheduler()
    {
        sleepFireCallback = state =>
        {
            var fiber = (RFiber)state!;
            if (!sleepTimers.TryRemove(fiber, out var t)) return;
            t.Dispose();
            ResumeSafe(fiber);
        };
        sleepCancelCallback = state =>
        {
            var fiber = (RFiber)state!;
            if (sleepTimers.TryRemove(fiber, out var t)) t.Dispose();
        };
        blockContinuationCallback = (task, state) =>
        {
            var fiber = (RFiber)state!;
            if (!blockedFibers.TryRemove(fiber, out _)) return;
            ResumeSafe(fiber, task.Status == TaskStatus.RanToCompletion
                ? task.Result
                : MRubyValue.Nil);
        };
        blockCancelCallback = state =>
        {
            ((BlockEntry)state!).TrySetCanceled();
        };
    }

    public void KernelSleep(TimeSpan duration, RFiber fiber, CancellationToken cancellationToken = default)
    {
        // TODO: Support TimeProvider for testability.
        var timer = new Timer(sleepFireCallback, fiber, duration, Timeout.InfiniteTimeSpan);
        sleepTimers[fiber] = timer;

        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.UnsafeRegister(sleepCancelCallback, fiber);
        }

        fiber.Yield();
    }

    public void Block(MRubyValue blocker, RFiber fiber, CancellationToken cancellationToken = default)
    {
        var entry = new BlockEntry();
        blockedFibers[fiber] = entry;

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

    static readonly WaitCallback YieldResumeCallback = static state => ResumeSafe((RFiber)state!);

    public void Yield(RFiber fiber, CancellationToken cancellationToken = default)
    {
        if (!cancellationToken.IsCancellationRequested)
        {
            ThreadPool.UnsafeQueueUserWorkItem(YieldResumeCallback, fiber);
        }
        fiber.Yield();
    }

    public void Await(
        ValueTask task,
        RFiber fiber,
        MRubyValue resumeValue = default,
        CancellationToken cancellationToken = default)
    {
        _ = AwaitAsync(task, fiber, resumeValue, cancellationToken);
        fiber.Yield();
    }

    public void Await<T>(
        ValueTask<T> task,
        RFiber fiber,
        Func<T, MRubyValue> mapResult,
        CancellationToken cancellationToken = default)
    {
        _ = AwaitAsync(task, fiber, mapResult, cancellationToken);
        fiber.Yield();
    }

    public void Await<T, TState>(
        ValueTask<T> task,
        RFiber fiber,
        Func<T, TState, MRubyValue> mapResult,
        TState state,
        CancellationToken cancellationToken = default)
    {
        _ = AwaitAsync(task, fiber, mapResult, state, cancellationToken);
        fiber.Yield();
    }

    static async Task AwaitAsync(ValueTask task, RFiber fiber, MRubyValue resumeValue, CancellationToken ct)
    {
        // Force an async boundary so the caller's fiber.Yield() runs
        // before Resume — synchronously-completed tasks would otherwise
        // call Resume before Yield, triggering a "double resume" error.
        await Task.Yield();
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            ResumeSafe(fiber, MRubyValue.Nil);
            return;
        }
        catch (Exception ex)
        {
            ResumeWithException(fiber, ex);
            return;
        }

        if (ct.IsCancellationRequested)
        {
            ResumeSafe(fiber, MRubyValue.Nil);
            return;
        }

        ResumeSafe(fiber, resumeValue);
    }

    static async Task AwaitAsync<T>(ValueTask<T> task, RFiber fiber, Func<T, MRubyValue> mapResult, CancellationToken ct)
    {
        await Task.Yield();
        T result;
        try
        {
            result = await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            ResumeSafe(fiber, MRubyValue.Nil);
            return;
        }
        catch (Exception ex)
        {
            ResumeWithException(fiber, ex);
            return;
        }

        if (ct.IsCancellationRequested)
        {
            ResumeSafe(fiber, MRubyValue.Nil);
            return;
        }

        MRubyValue mapped;
        try
        {
            mapped = mapResult(result);
        }
        catch (Exception ex)
        {
            ResumeWithException(fiber, ex);
            return;
        }
        ResumeSafe(fiber, mapped);
    }

    static async Task AwaitAsync<T, TState>(
        ValueTask<T> task, RFiber fiber,
        Func<T, TState, MRubyValue> mapResult, TState state,
        CancellationToken ct)
    {
        await Task.Yield();
        T result;
        try
        {
            result = await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            ResumeSafe(fiber, MRubyValue.Nil);
            return;
        }
        catch (Exception ex)
        {
            ResumeWithException(fiber, ex);
            return;
        }

        if (ct.IsCancellationRequested)
        {
            ResumeSafe(fiber, MRubyValue.Nil);
            return;
        }

        MRubyValue mapped;
        try
        {
            mapped = mapResult(result, state);
        }
        catch (Exception ex)
        {
            ResumeWithException(fiber, ex);
            return;
        }
        ResumeSafe(fiber, mapped);
    }

    static void ResumeSafe(RFiber fiber, MRubyValue value = default)
    {
        if (!fiber.IsAlive) return;
        try { fiber.Resume(value); }
        catch
        {
            // Resume's exception already routed through resumeSource for
            // WaitForTerminate observers; swallow here so the threadpool /
            // timer infrastructure doesn't crash the process.
        }
    }

    static void ResumeWithException(RFiber fiber, Exception ex)
    {
        if (!fiber.IsAlive) return;
        var state = fiber.MRubyState;
        var msg = ex.Message ?? ex.GetType().Name;
        var rex = new RException(state.NewString(System.Text.Encoding.UTF8.GetBytes(msg)),
            state.GetExceptionClass(Names.RuntimeError));
        fiber.PendingException = rex;
        try { fiber.Resume(); }
        catch
        {
            // PendingException raises MRubyLongJumpException out of MoveNext.
            // Resume's catch already sets resumeSource.Exception; swallow
            // here to keep the threadpool callback safe.
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
    }
}
