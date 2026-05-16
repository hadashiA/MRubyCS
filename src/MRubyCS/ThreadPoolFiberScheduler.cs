using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MRubyCS;

/// <summary>
/// Default <see cref="IMRubyFiberScheduler"/> implementation backed by .NET
/// thread-pool tasks and timers. Suitable for tests, CLI tools, and any
/// host where the VM is only touched from inside fiber bodies. Resume
/// callbacks fire on threadpool threads.
/// </summary>
public sealed class ThreadPoolFiberScheduler : IMRubyFiberScheduler
{
    MRubyState mrb = default!;
    readonly ConcurrentDictionary<RFiber, BlockEntry> blockedFibers = new();
    readonly ConcurrentDictionary<RFiber, Timer> sleepTimers = new();

    // Cached delegates: capture `this` once, never box ValueTuple<this,...>
    // when marshaling state through Timer / UnsafeRegister / ContinueWith.
    readonly TimerCallback sleepFireCallback;
    readonly Action<object?> sleepCancelCallback;
    readonly Action<Task<MRubyValue>, object?> blockContinuationCallback;

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
            switch (task.Status)
            {
                case TaskStatus.RanToCompletion:
                    ResumeSafe(fiber, task.Result);
                    break;
                case TaskStatus.Faulted:
                    ResumeWithException(fiber, task.Exception?.InnerException ?? task.Exception!);
                    break;
                default:
                    ResumeSafe(fiber, MRubyValue.Nil);
                    break;
            }
        };
    }

    public void Attach(MRubyState mrb) => this.mrb = mrb;

    public void KernelSleep(TimeSpan duration, CancellationToken cancellationToken = default)
    {
        var fiber = mrb.CurrentFiber;
        // TODO: Support TimeProvider for testability.
        var timer = new Timer(sleepFireCallback, fiber, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        if (!sleepTimers.TryAdd(fiber, timer))
        {
            timer.Dispose();
            ThrowAlreadyParked(fiber, "KernelSleep");
        }
        // Arm only after the registration succeeds, so the fire callback can't
        // race ahead and observe a missing dict entry.
        timer.Change(duration, Timeout.InfiniteTimeSpan);

        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.UnsafeRegister(sleepCancelCallback, fiber);
        }

        fiber.Yield();
    }

    public FiberContinuation Suspend()
    {
        var fiber = mrb.CurrentFiber;
        var entry = new BlockEntry();
        if (!blockedFibers.TryAdd(fiber, entry))
        {
            ThrowAlreadyParked(fiber, "Suspend");
        }

        entry.Task.ContinueWith(blockContinuationCallback, fiber, CancellationToken.None,
            TaskContinuationOptions.None, TaskScheduler.Default);

        var continuation = new FiberContinuation(this, fiber);
        fiber.Yield();
        return continuation;
    }

    public void ResumeFiber(RFiber fiber, MRubyValue value)
    {
        if (blockedFibers.TryGetValue(fiber, out var entry)) entry.TrySetResult(value);
    }

    public void CancelFiber(RFiber fiber, CancellationToken cancellationToken)
    {
        if (blockedFibers.TryGetValue(fiber, out var entry)) entry.TrySetCanceled(cancellationToken);
    }

    public void FailFiber(RFiber fiber, Exception exception)
    {
        if (blockedFibers.TryGetValue(fiber, out var entry)) entry.TrySetException(exception);
    }

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    static void ThrowAlreadyParked(RFiber fiber, string op) =>
        throw new InvalidOperationException(
            $"{op}: fiber is already parked under this scheduler. Each park must be matched by Resume/SetCancelled/SetException/cancel before another can be issued.");

    static readonly WaitCallback YieldResumeCallback = static state => ResumeSafe((RFiber)state!);

    public void Yield(CancellationToken cancellationToken = default)
    {
        var fiber = mrb.CurrentFiber;
        if (!cancellationToken.IsCancellationRequested)
        {
            ThreadPool.UnsafeQueueUserWorkItem(YieldResumeCallback, fiber);
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
        _ = ReadStreamAsync(fiber, stream, maxBytes, disposeStream, cancellationToken);
        fiber.Yield();
    }

    static async Task ReadStreamAsync(
        RFiber fiber, Stream stream, int maxBytes,
        bool disposeStream, CancellationToken ct)
    {
        // Force async boundary so the caller's fiber.Yield() runs first.
        await Task.Yield();
        var buffer = ArrayPool<byte>.Shared.Rent(maxBytes);
        try
        {
            int bytesRead;
            try { bytesRead = await stream.ReadAsync(buffer.AsMemory(0, maxBytes), ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { if (disposeStream) stream.Dispose(); ResumeSafe(fiber, MRubyValue.Nil); return; }
            catch (Exception ex) { if (disposeStream) stream.Dispose(); ResumeWithException(fiber, ex); return; }

            if (disposeStream) stream.Dispose();
            // EOF → nil, otherwise a String of the read bytes (IO#read(n) semantics).
            ResumeSafe(fiber, bytesRead == 0
                ? MRubyValue.Nil
                : new MRubyValue(fiber.MRubyState.NewString(buffer.AsSpan(0, bytesRead))));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public void ReadStreamToEnd(
        Stream stream,
        bool disposeStream = false,
        CancellationToken cancellationToken = default)
    {
        var fiber = mrb.CurrentFiber;
        _ = ReadStreamToEndAsync(fiber, stream, disposeStream, cancellationToken);
        fiber.Yield();
    }

    static async Task ReadStreamToEndAsync(
        RFiber fiber, Stream stream, bool disposeStream, CancellationToken ct)
    {
        await Task.Yield();
        var writer = new ArrayBufferWriter<byte>();
        try
        {
            while (true)
            {
                var mem = writer.GetMemory(4096);
                var read = await stream.ReadAsync(mem, ct).ConfigureAwait(false);
                if (read == 0) break;
                writer.Advance(read);
            }
        }
        catch (OperationCanceledException) { if (disposeStream) stream.Dispose(); ResumeSafe(fiber, MRubyValue.Nil); return; }
        catch (Exception ex) { if (disposeStream) stream.Dispose(); ResumeWithException(fiber, ex); return; }

        if (disposeStream) stream.Dispose();
        ResumeSafe(fiber, new MRubyValue(fiber.MRubyState.NewString(writer.WrittenSpan)));
    }

    public void WriteStream(
        Stream stream,
        ReadOnlyMemory<byte> data,
        bool disposeStream = false,
        CancellationToken cancellationToken = default)
    {
        var fiber = mrb.CurrentFiber;
        _ = WriteStreamAsync(fiber, stream, data, disposeStream, cancellationToken);
        fiber.Yield();
    }

    static async Task WriteStreamAsync(
        RFiber fiber, Stream stream, ReadOnlyMemory<byte> data,
        bool disposeStream, CancellationToken ct)
    {
        await Task.Yield();
        try { await stream.WriteAsync(data, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { if (disposeStream) stream.Dispose(); ResumeSafe(fiber, MRubyValue.Nil); return; }
        catch (Exception ex) { if (disposeStream) stream.Dispose(); ResumeWithException(fiber, ex); return; }
        if (disposeStream) stream.Dispose();
        ResumeSafe(fiber, new MRubyValue((long)data.Length));
    }

    static void ResumeSafe(RFiber fiber, MRubyValue value = default)
    {
        if (!fiber.IsAlive) return;
        // Exceptions are routed via resumeSource for WaitForTerminate observers;
        // swallow here so threadpool / timer callbacks don't crash the process.
        try { fiber.Resume(value); }
        catch { }
    }

    static void ResumeWithException(RFiber fiber, Exception ex)
    {
        if (!fiber.IsAlive) return;
        var state = fiber.MRubyState;
        var msg = ex.Message ?? ex.GetType().Name;
        fiber.PendingException = new RException(
            state.NewString(System.Text.Encoding.UTF8.GetBytes(msg)),
            state.GetExceptionClass(Names.RuntimeError));
        try { fiber.Resume(); }
        catch { }
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
