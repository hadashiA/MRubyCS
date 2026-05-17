using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MRubyCS;

/// <summary>
/// <see cref="IMRubyFiberScheduler"/> that marshals all
/// <see cref="RFiber.Resume"/> calls through a captured
/// <see cref="SynchronizationContext"/> (WPF / WinForms / pumped loop).
/// The context is captured at construction; pass it explicitly or call
/// from the owning thread. VM work always runs on the captured context;
/// only internal wait primitives (<see cref="Task.Delay(TimeSpan, CancellationToken)"/>,
/// <see cref="TaskCompletionSource{TResult}"/>) may briefly touch the threadpool.
/// </summary>
public sealed class SynchronizationContextFiberScheduler : IMRubyFiberScheduler
{
    readonly SynchronizationContext context;
    readonly ConcurrentDictionary<RFiber, BlockEntry> blockedFibers = new();
    readonly ConcurrentDictionary<RFiber, CancellationTokenSource> sleepCancellations = new();

    readonly SendOrPostCallback postResumeCallback;
    MRubyState mrb = default!;

    sealed class BlockEntry() : TaskCompletionSource<MRubyValue>(TaskCreationOptions.RunContinuationsAsynchronously);

    // Carries data through SynchronizationContext.Post without boxing a
    // ValueTuple. Pooled below to avoid per-resume allocations.
    sealed class PostState
    {
        public RFiber Fiber = default!;
        public MRubyValue Value;
    }

    readonly ConcurrentStack<PostState> postStatePool = new();
    const int PostStatePoolMax = 64;

    PostState RentPostState(RFiber fiber, MRubyValue value)
    {
        if (!postStatePool.TryPop(out var s)) s = new PostState();
        s.Fiber = fiber;
        s.Value = value;
        return s;
    }

    void ReturnPostState(PostState s)
    {
        s.Fiber = null!;
        s.Value = default;
        if (postStatePool.Count < PostStatePoolMax) postStatePool.Push(s);
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
            ReturnPostState(ps);
            if (!fiber.IsAlive) return;
            try { fiber.Resume(value); }
            catch { /* propagated via resumeSource */ }
        };
    }

    public void Attach(MRubyState mrb) => this.mrb = mrb;

    public void KernelSleep(TimeSpan duration, CancellationToken cancellationToken = default)
    {
        var fiber = mrb.CurrentFiber;
        var cts = cancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : new CancellationTokenSource();
        if (!sleepCancellations.TryAdd(fiber, cts))
        {
            cts.Dispose();
            ThrowAlreadyParked(fiber, "KernelSleep");
        }

        _ = SleepAsync(fiber, duration, cts);
        fiber.Yield();
        return;

        async ValueTask SleepAsync(RFiber fiber, TimeSpan duration, CancellationTokenSource cts)
        {
            try
            {
                // Force async boundary so the caller-side fiber.Yield() runs
                // before our resume; otherwise a zero-duration Delay would
                // trigger a "double resume" on the VM.
                await Task.Yield();
                try { await Task.Delay(duration, cts.Token); }
                catch (OperationCanceledException) { /* fall through to resume */ }

                if (!sleepCancellations.TryRemove(fiber, out _)) return;
                TryResume(fiber, MRubyValue.Nil);
            }
            finally
            {
                cts.Dispose();
            }
        }
    }

    public FiberContinuation Suspend()
    {
        var fiber = mrb.CurrentFiber;
        var entry = new BlockEntry();
        if (!blockedFibers.TryAdd(fiber, entry))
        {
            ThrowAlreadyParked(fiber, "Suspend");
        }

        _ = RunAsync();
        var continuation = new FiberContinuation(this, fiber);
        fiber.Yield();
        return continuation;

        async ValueTask RunAsync()
        {
            // Force async boundary so the caller-side fiber.Yield() runs
            // before our resume; otherwise an already-completed entry.Task
            // would trigger a "double resume" on the VM.
            await Task.Yield();
            if (!blockedFibers.TryRemove(fiber, out _)) return;

            MRubyValue value;
            try { value = await entry.Task; }
            catch (OperationCanceledException) { TryResume(fiber, MRubyValue.Nil); return; }
            catch (Exception ex) { TryResumeWithException(fiber, ex); return; }
            TryResume(fiber, value);
        }
    }

    public void Await(Func<ValueTask<MRubyValue>> body)
    {
        if (body is null) throw new ArgumentNullException(nameof(body));
        var continuation = Suspend();
        // Post the body kickoff to the captured context. context.Post is
        // asynchronous, so the caller-side fiber.Yield() runs first — no
        // need for an explicit Task.Yield boundary inside body. Body's
        // inner awaits naturally capture the context (no ConfigureAwait).
        context.Post(awaitDispatchCallback, new AwaitState(body, continuation));
    }

    sealed class AwaitState(Func<ValueTask<MRubyValue>> body, FiberContinuation continuation)
    {
        public readonly Func<ValueTask<MRubyValue>> Body = body;
        public readonly FiberContinuation Continuation = continuation;
    }

    static readonly SendOrPostCallback awaitDispatchCallback = static s =>
    {
        var st = (AwaitState)s!;
        _ = AwaitAsync(st.Body, st.Continuation);
    };

    static async ValueTask AwaitAsync(Func<ValueTask<MRubyValue>> body, FiberContinuation continuation)
    {
        MRubyValue value;
        try { value = await body(); }
        catch (OperationCanceledException ex) { continuation.SetCancelled(ex.CancellationToken); return; }
        catch (Exception ex) { continuation.SetException(ex); return; }
        continuation.Resume(value);
    }

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    static void ThrowAlreadyParked(RFiber fiber, string op) =>
        throw new InvalidOperationException(
            $"{op}: fiber is already parked under this scheduler. Each park must be matched by Resume/SetCancelled/SetException/cancel before another can be issued.");

    public void SetResult(RFiber fiber, MRubyValue value)
    {
        if (blockedFibers.TryGetValue(fiber, out var entry)) entry.TrySetResult(value);
    }

    public void SetCancelled(RFiber fiber, CancellationToken cancellationToken)
    {
        if (blockedFibers.TryGetValue(fiber, out var entry)) entry.TrySetCanceled(cancellationToken);
    }

    public void SetException(RFiber fiber, Exception exception)
    {
        if (blockedFibers.TryGetValue(fiber, out var entry)) entry.TrySetException(exception);
    }

    public void Yield(CancellationToken cancellationToken = default)
    {
        var fiber = mrb.CurrentFiber;
        if (!cancellationToken.IsCancellationRequested)
        {
            ScheduleResume(fiber, default);
        }
        fiber.Yield();
    }

    public void ReadStream(
        Stream stream,
        int maxBytes,
        bool disposeStream = false,
        CancellationToken cancellationToken = default)
    {
        var fiber = mrb.CurrentFiber;
        _ = RunAsync();
        fiber.Yield();
        return;

        async ValueTask RunAsync()
        {
            await Task.Yield();
            var buffer = ArrayPool<byte>.Shared.Rent(maxBytes);
            try
            {
                int bytesRead;
                try { bytesRead = await stream.ReadAsync(buffer.AsMemory(0, maxBytes), cancellationToken); }
                catch (OperationCanceledException) { if (disposeStream) stream.Dispose(); TryResume(fiber, MRubyValue.Nil); return; }
                catch (Exception ex) { if (disposeStream) stream.Dispose(); TryResumeWithException(fiber, ex); return; }

                if (disposeStream) stream.Dispose();
                TryResume(fiber, bytesRead == 0
                    ? MRubyValue.Nil
                    : new MRubyValue(fiber.MRubyState.NewString(buffer.AsSpan(0, bytesRead))));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    public void ReadStreamToEnd(
        Stream stream,
        bool disposeStream = false,
        CancellationToken cancellationToken = default)
    {
        var fiber = mrb.CurrentFiber;
        _ = RunAsync();
        fiber.Yield();
        return;

        async ValueTask RunAsync()
        {
            await Task.Yield();
            var writer = new ArrayBufferWriter<byte>();
            try
            {
                while (true)
                {
                    var mem = writer.GetMemory(4096);
                    var read = await stream.ReadAsync(mem, cancellationToken);
                    if (read == 0) break;
                    writer.Advance(read);
                }
            }
            catch (OperationCanceledException) { if (disposeStream) stream.Dispose(); TryResume(fiber, MRubyValue.Nil); return; }
            catch (Exception ex) { if (disposeStream) stream.Dispose(); TryResumeWithException(fiber, ex); return; }

            if (disposeStream) stream.Dispose();
            TryResume(fiber, new MRubyValue(fiber.MRubyState.NewString(writer.WrittenSpan)));
        }
    }

    public void WriteStream(
        Stream stream,
        ReadOnlyMemory<byte> data,
        bool disposeStream = false,
        CancellationToken cancellationToken = default)
    {
        var fiber = mrb.CurrentFiber;
        _ = RunAsync();
        fiber.Yield();
        return;

        async ValueTask RunAsync()
        {
            await Task.Yield();
            try { await stream.WriteAsync(data, cancellationToken); }
            catch (OperationCanceledException) { if (disposeStream) stream.Dispose(); TryResume(fiber, MRubyValue.Nil); return; }
            catch (Exception ex) { if (disposeStream) stream.Dispose(); TryResumeWithException(fiber, ex); return; }
            if (disposeStream) stream.Dispose();
            TryResume(fiber, new MRubyValue((long)data.Length));
        }
    }

    static void TryResume(RFiber fiber, MRubyValue value)
    {
        if (!fiber.IsAlive) return;
        try { fiber.Resume(value); }
        catch { /* propagated via resumeSource */ }
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
        catch { /* propagated via resumeSource */ }
    }

    void ScheduleResume(RFiber fiber, MRubyValue value)
    {
        context.Post(postResumeCallback, RentPostState(fiber, value));
    }

    public void Dispose()
    {
        foreach (var kv in blockedFibers) kv.Value.TrySetCanceled();
        blockedFibers.Clear();
        foreach (var kv in sleepCancellations) kv.Value.Cancel();
        sleepCancellations.Clear();
    }
}