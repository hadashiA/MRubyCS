using System;
using System.IO;
using System.Threading;

namespace MRubyCS;

/// <summary>
/// CRuby-style <c>Fiber::Scheduler</c> hook surface. Hooks return immediately
/// after recording the request; the scheduler later resumes the fiber on the
/// VM thread. Convention: <see cref="RFiber"/> first parameter,
/// <see cref="CancellationToken"/> last.
/// </summary>
/// <remarks>
/// On exception inside a stream hook, set <see cref="RFiber.PendingException"/>
/// and call <see cref="RFiber.Resume()"/>; the VM re-raises so Ruby
/// <c>rescue</c> can catch it.
/// </remarks>
public interface IMRubyFiberScheduler : IDisposable
{
    /// <summary><c>Kernel#sleep</c>: resume after <paramref name="duration"/>.</summary>
    void KernelSleep(RFiber fiber, TimeSpan duration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Park <paramref name="fiber"/> until <see cref="Unblock"/> (or cancel).
    /// Arrange the <see cref="Unblock"/> plumbing <b>before</b> calling
    /// <c>Block</c>; the call yields internally.
    /// </summary>
    void Block(RFiber fiber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Wake a fiber parked in <see cref="Block"/>; <paramref name="resumeValue"/>
    /// becomes the apparent return of the C# primitive that parked it.
    /// May be called from any thread.
    /// </summary>
    void Unblock(RFiber fiber, MRubyValue resumeValue = default);

    /// <summary><c>Thread.pass</c>: cooperative yield.</summary>
    void Yield(RFiber fiber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bounded async read: reads up to <paramref name="maxBytes"/> from
    /// <paramref name="stream"/> and resumes the fiber with a <c>String</c>
    /// of the read bytes (or <c>nil</c> at EOF, matching <c>IO#read(n)</c>).
    /// </summary>
    void ReadStream(
        RFiber fiber,
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
        RFiber fiber,
        Stream stream,
        bool disposeStream = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Async write of <paramref name="data"/> to <paramref name="stream"/>.
    /// Resumes with bytes-written (<c>Integer</c>, equal to
    /// <c>data.Length</c> on success).
    /// </summary>
    void WriteStream(
        RFiber fiber,
        Stream stream,
        ReadOnlyMemory<byte> data,
        bool disposeStream = false,
        CancellationToken cancellationToken = default);
}
