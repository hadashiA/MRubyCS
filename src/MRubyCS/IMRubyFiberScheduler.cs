using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MRubyCS;

/// <summary>
/// CRuby-style Fiber scheduler hook surface, adapted for C# integration.
/// Hosts implement this to make blocking Ruby primitives (currently
/// <c>Kernel#sleep</c>, <c>Fiber.pass</c>, stream I/O) cooperate with an
/// async runtime such as UniTask.
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
/// <b>Projection delegates are synchronous</b>. The Stream I/O hooks accept
/// <see cref="Func{T1, T2, TResult}"/> projections that build the Ruby
/// resume value from the byte count + caller state; the scheduler invokes
/// them on the VM thread right before resuming. Because they are non-async
/// (no <c>ValueTask</c> return), the caller can't introduce its own awaits
/// — threading remains under the scheduler's exclusive control.
/// </para>
/// <para>
/// <b>Exception delivery</b>: if a stream I/O hook's await throws,
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
    /// Park <paramref name="fiber"/> until <see cref="Unblock"/> is called,
    /// or until <paramref name="cancellationToken"/> fires.
    /// </summary>
    /// <remarks>
    /// Callers must arrange the eventual <see cref="Unblock"/> (e.g. queue
    /// a <c>Task</c>) <b>before</b> invoking <c>Block</c>; otherwise the
    /// caller and the unblock plumbing race for the recorded-park state.
    /// Apply a deadline by combining the caller's
    /// <see cref="CancellationToken"/> with one that auto-cancels (e.g.
    /// <see cref="CancellationTokenSource.CancelAfter(TimeSpan)"/>).
    /// </remarks>
    void Block(RFiber fiber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Wake a fiber currently parked in <see cref="Block"/>.
    /// <paramref name="resumeValue"/> becomes the return value of the
    /// C#-side primitive that called <see cref="Block"/>.
    /// May be called from any thread.
    /// </summary>
    void Unblock(RFiber fiber, MRubyValue resumeValue = default);

    /// <summary>
    /// Cooperative yield (<c>Thread.pass</c> equivalent): park the current
    /// fiber and arrange for it to be resumed at the next available
    /// opportunity, letting other in-flight fibers and host work run.
    /// </summary>
    void Yield(RFiber fiber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bounded async read: fill (up to) <paramref name="buffer"/>'s length
    /// from <paramref name="stream"/>. When complete, the scheduler invokes
    /// <paramref name="projection"/> on the VM thread with the actual byte
    /// count + <paramref name="state"/>, and resumes the fiber with the
    /// returned <see cref="MRubyValue"/>. <paramref name="buffer"/> is
    /// caller-owned and must outlive the resume.
    /// </summary>
    /// <param name="disposeStream">
    /// When <c>true</c>, the scheduler disposes <paramref name="stream"/>
    /// once the read completes (or fails) — strictly before the projection
    /// runs and before the fiber resumes. Use for caller-opened streams
    /// whose lifetime is bound to this single operation.
    /// </param>
    void ReadStream<TState>(
        Stream stream,
        Memory<byte> buffer,
        TState state,
        Func<int, TState, MRubyValue> projection,
        RFiber fiber,
        CancellationToken cancellationToken = default,
        bool disposeStream = false);

    /// <summary>
    /// Unbounded async read: read from <paramref name="stream"/> into
    /// <paramref name="writer"/> until EOF (or
    /// <paramref name="cancellationToken"/>). When complete, the scheduler
    /// invokes <paramref name="projection"/> on the VM thread with the
    /// total byte count + <paramref name="state"/>, and resumes the fiber
    /// with the returned <see cref="MRubyValue"/>. The scheduler may
    /// allocate internal chunk buffers as needed.
    /// </summary>
    /// <param name="disposeStream">
    /// When <c>true</c>, the scheduler disposes <paramref name="stream"/>
    /// once the read completes (or fails), before projection / resume.
    /// </param>
    void ReadStreamToEnd<TState>(
        Stream stream,
        IBufferWriter<byte> writer,
        TState state,
        Func<long, TState, MRubyValue> projection,
        RFiber fiber,
        CancellationToken cancellationToken = default,
        bool disposeStream = false);

    /// <summary>
    /// Async write: push <paramref name="data"/> into
    /// <paramref name="stream"/>. On completion the fiber is resumed with
    /// the bytes-written count as an <c>Integer</c> (always equal to
    /// <c>data.Length</c> on success).
    /// </summary>
    /// <param name="disposeStream">
    /// When <c>true</c>, the scheduler disposes <paramref name="stream"/>
    /// (and so flushes its buffers) once the write completes (or fails),
    /// before the fiber resumes.
    /// </param>
    void WriteStream(
        Stream stream,
        ReadOnlyMemory<byte> data,
        RFiber fiber,
        CancellationToken cancellationToken = default,
        bool disposeStream = false);
}
