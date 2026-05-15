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

    public void Block(RFiber fiber, CancellationToken cancellationToken = default)
    {
        var entry = new BlockEntry();
        if (!blockedFibers.TryAdd(fiber, entry))
        {
            ThrowAlreadyParked(fiber, "Block");
        }

        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.UnsafeRegister(blockCancelCallback, entry);
        }

        entry.Task.ContinueWith(blockContinuationCallback, fiber, CancellationToken.None,
            TaskContinuationOptions.None, TaskScheduler.Default);

        fiber.Yield();
    }

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    static void ThrowAlreadyParked(RFiber fiber, string op) =>
        throw new InvalidOperationException(
            $"{op}: fiber is already parked under this scheduler. Each park must be matched by an Unblock/timeout/cancel before another can be issued.");

    public void Unblock(RFiber fiber, MRubyValue resumeValue = default)
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

    public void ReadStream<TState>(
        Stream stream,
        Memory<byte> buffer,
        TState state,
        Func<int, TState, MRubyValue> projection,
        RFiber fiber,
        CancellationToken cancellationToken = default,
        bool disposeStream = false)
    {
        _ = ReadStreamAsync(stream, buffer, state, projection, fiber, cancellationToken, disposeStream);
        fiber.Yield();
    }

    static async Task ReadStreamAsync<TState>(
        Stream stream, Memory<byte> buffer,
        TState state, Func<int, TState, MRubyValue> projection,
        RFiber fiber, CancellationToken ct, bool disposeStream)
    {
        // Force async boundary so the caller's fiber.Yield() runs before
        // Resume — synchronously-completed reads would otherwise Resume
        // before Yield, triggering a "double resume" error.
        await Task.Yield();
        int bytesRead;
        try { bytesRead = await stream.ReadAsync(buffer, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { if (disposeStream) stream.Dispose(); ResumeSafe(fiber, MRubyValue.Nil); return; }
        catch (Exception ex) { if (disposeStream) stream.Dispose(); ResumeWithException(fiber, ex); return; }

        if (disposeStream) stream.Dispose();
        MRubyValue result;
        try { result = projection(bytesRead, state); }
        catch (Exception ex) { ResumeWithException(fiber, ex); return; }
        ResumeSafe(fiber, result);
    }

    public void ReadStreamToEnd<TState>(
        Stream stream,
        IBufferWriter<byte> writer,
        TState state,
        Func<long, TState, MRubyValue> projection,
        RFiber fiber,
        CancellationToken cancellationToken = default,
        bool disposeStream = false)
    {
        _ = ReadStreamToEndAsync(stream, writer, state, projection, fiber, cancellationToken, disposeStream);
        fiber.Yield();
    }

    static async Task ReadStreamToEndAsync<TState>(
        Stream stream, IBufferWriter<byte> writer,
        TState state, Func<long, TState, MRubyValue> projection,
        RFiber fiber, CancellationToken ct, bool disposeStream)
    {
        await Task.Yield();
        long total = 0;
        try
        {
            while (true)
            {
                var mem = writer.GetMemory(4096);
                var read = await stream.ReadAsync(mem, ct).ConfigureAwait(false);
                if (read == 0) break;
                writer.Advance(read);
                total += read;
            }
        }
        catch (OperationCanceledException) { if (disposeStream) stream.Dispose(); ResumeSafe(fiber, MRubyValue.Nil); return; }
        catch (Exception ex) { if (disposeStream) stream.Dispose(); ResumeWithException(fiber, ex); return; }

        if (disposeStream) stream.Dispose();
        MRubyValue result;
        try { result = projection(total, state); }
        catch (Exception ex) { ResumeWithException(fiber, ex); return; }
        ResumeSafe(fiber, result);
    }

    public void WriteStream(
        Stream stream,
        ReadOnlyMemory<byte> data,
        RFiber fiber,
        CancellationToken cancellationToken = default,
        bool disposeStream = false)
    {
        _ = WriteStreamAsync(stream, data, fiber, cancellationToken, disposeStream);
        fiber.Yield();
    }

    static async Task WriteStreamAsync(
        Stream stream, ReadOnlyMemory<byte> data, RFiber fiber, CancellationToken ct, bool disposeStream)
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
