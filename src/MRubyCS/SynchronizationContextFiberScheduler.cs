using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace MRubyCS;

/// <summary>
/// <see cref="IMRubyFiberScheduler"/> that marshals every
/// <see cref="RFiber.Resume"/> call through a captured
/// <see cref="SynchronizationContext"/>.
/// Use this for hosts that own a UI / loop thread accessible via
/// <c>SynchronizationContext.Current</c> — WPF, WinForms, classic ASP.NET,
/// or any custom loop that publishes a sync context.
/// </summary>
/// <remarks>
/// <para>
/// Timer / continuation callbacks still fire on the .NET threadpool; the
/// scheduler then posts the actual <see cref="RFiber.Resume"/> back to the
/// captured context so all VM work stays on the host's preferred thread.
/// </para>
/// <para>
/// The <see cref="SynchronizationContext"/> is captured at construction
/// time. Pass an explicit context to the constructor or call from the
/// thread that owns it so <see cref="SynchronizationContext.Current"/>
/// resolves correctly.
/// </para>
/// </remarks>
public sealed class SynchronizationContextFiberScheduler : IMRubyFiberScheduler
{
    readonly SynchronizationContext context;
    readonly ConcurrentDictionary<RFiber, BlockEntry> blockedFibers = new();
    readonly ConcurrentDictionary<RFiber, Timer> sleepTimers = new();

    readonly TimerCallback sleepFireCallback;
    readonly Action<object?> sleepCancelCallback;
    readonly Action<Task<MRubyValue>, object?> blockContinuationCallback;
    readonly Action<object?> blockCancelCallback;
    readonly SendOrPostCallback postResumeCallback;

    sealed class BlockEntry() : TaskCompletionSource<MRubyValue>(TaskCreationOptions.RunContinuationsAsynchronously);

    // Carries the (fiber, value) tuple through SynchronizationContext.Post
    // without boxing a ValueTuple. Allocated per resume; pooled if profiling
    // says it matters.
    sealed class PostState
    {
        public RFiber Fiber = default!;
        public MRubyValue Value;
    }

    /// <summary>
    /// Captures <see cref="SynchronizationContext.Current"/>. Throws if
    /// none is available.
    /// </summary>
    public SynchronizationContextFiberScheduler()
        : this(SynchronizationContext.Current
              ?? throw new InvalidOperationException(
                  "SynchronizationContext.Current is null. Construct on a thread that owns one, or pass an explicit context."))
    {
    }

    public SynchronizationContextFiberScheduler(SynchronizationContext context)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));

        postResumeCallback = state =>
        {
            var ps = (PostState)state!;
            var fiber = ps.Fiber;
            var value = ps.Value;
            ps.Fiber = null!;
            try { if (fiber.IsAlive) fiber.Resume(value); }
            catch { /* exception already routed via resumeSource */ }
        };

        sleepFireCallback = s =>
        {
            var fiber = (RFiber)s!;
            if (!sleepTimers.TryRemove(fiber, out var t)) return;
            t.Dispose();
            ScheduleResume(fiber, default);
        };
        sleepCancelCallback = s =>
        {
            var fiber = (RFiber)s!;
            if (sleepTimers.TryRemove(fiber, out var t)) t.Dispose();
        };
        blockContinuationCallback = (task, s) =>
        {
            var fiber = (RFiber)s!;
            if (!blockedFibers.TryRemove(fiber, out _)) return;
            ScheduleResume(fiber, task.Status == TaskStatus.RanToCompletion
                ? task.Result
                : MRubyValue.Nil);
        };
        blockCancelCallback = s =>
        {
            ((BlockEntry)s!).TrySetCanceled();
        };
    }

    public void KernelSleep(TimeSpan duration, RFiber fiber, CancellationToken cancellationToken = default)
    {
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

    public void Yield(RFiber fiber, CancellationToken cancellationToken = default)
    {
        if (!cancellationToken.IsCancellationRequested)
        {
            ScheduleResume(fiber, default);
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

    async Task AwaitAsync(ValueTask task, RFiber fiber, MRubyValue resumeValue, CancellationToken ct)
    {
        await Task.Yield();
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            ScheduleResume(fiber, MRubyValue.Nil);
            return;
        }
        catch (Exception ex)
        {
            ScheduleResumeWithException(fiber, ex);
            return;
        }

        if (ct.IsCancellationRequested)
        {
            ScheduleResume(fiber, MRubyValue.Nil);
            return;
        }
        ScheduleResume(fiber, resumeValue);
    }

    async Task AwaitAsync<T>(ValueTask<T> task, RFiber fiber, Func<T, MRubyValue> mapResult, CancellationToken ct)
    {
        await Task.Yield();
        T result;
        try { result = await task.ConfigureAwait(false); }
        catch (OperationCanceledException) { ScheduleResume(fiber, MRubyValue.Nil); return; }
        catch (Exception ex) { ScheduleResumeWithException(fiber, ex); return; }

        if (ct.IsCancellationRequested) { ScheduleResume(fiber, MRubyValue.Nil); return; }

        // The projection runs on the captured context (where it's safe to
        // touch MRubyState). Post the whole "map + resume" step together.
        context.Post(state =>
        {
            var ps = (MapResumeState<T>)state!;
            try
            {
                if (!ps.Fiber.IsAlive) return;
                ps.Fiber.Resume(ps.Map(ps.Result));
            }
            catch { /* propagated via resumeSource */ }
        }, new MapResumeState<T> { Fiber = fiber, Result = result, Map = mapResult });
    }

    async Task AwaitAsync<T, TState>(
        ValueTask<T> task, RFiber fiber,
        Func<T, TState, MRubyValue> mapResult, TState state,
        CancellationToken ct)
    {
        await Task.Yield();
        T result;
        try { result = await task.ConfigureAwait(false); }
        catch (OperationCanceledException) { ScheduleResume(fiber, MRubyValue.Nil); return; }
        catch (Exception ex) { ScheduleResumeWithException(fiber, ex); return; }

        if (ct.IsCancellationRequested) { ScheduleResume(fiber, MRubyValue.Nil); return; }

        context.Post(s =>
        {
            var ps = (MapResumeState<T, TState>)s!;
            try
            {
                if (!ps.Fiber.IsAlive) return;
                ps.Fiber.Resume(ps.Map(ps.Result, ps.MapState));
            }
            catch { /* propagated via resumeSource */ }
        }, new MapResumeState<T, TState> { Fiber = fiber, Result = result, Map = mapResult, MapState = state });
    }

    sealed class MapResumeState<T>
    {
        public RFiber Fiber = default!;
        public T Result = default!;
        public Func<T, MRubyValue> Map = default!;
    }

    sealed class MapResumeState<T, TState>
    {
        public RFiber Fiber = default!;
        public T Result = default!;
        public Func<T, TState, MRubyValue> Map = default!;
        public TState MapState = default!;
    }

    void ScheduleResume(RFiber fiber, MRubyValue value)
    {
        // PostState allocation per call. SynchronizationContext.Post itself
        // typically allocates; this addition is negligible.
        context.Post(postResumeCallback, new PostState { Fiber = fiber, Value = value });
    }

    void ScheduleResumeWithException(RFiber fiber, Exception ex)
    {
        if (!fiber.IsAlive) return;
        var state = fiber.MRubyState;
        var msg = ex.Message ?? ex.GetType().Name;
        context.Post(s =>
        {
            var entry = (PostState)s!;
            try
            {
                if (!entry.Fiber.IsAlive) return;
                entry.Fiber.PendingException = new RException(
                    state.NewString(System.Text.Encoding.UTF8.GetBytes(msg)),
                    state.GetExceptionClass(Names.RuntimeError));
                entry.Fiber.Resume();
            }
            catch { /* propagated via resumeSource */ }
        }, new PostState { Fiber = fiber, Value = default });
    }

    public void Dispose()
    {
        foreach (var kv in blockedFibers) kv.Value.TrySetCanceled();
        blockedFibers.Clear();
        foreach (var kv in sleepTimers) kv.Value.Dispose();
        sleepTimers.Clear();
    }
}
