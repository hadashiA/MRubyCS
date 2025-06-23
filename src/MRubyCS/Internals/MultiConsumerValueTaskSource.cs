using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace MRubyCS.Internals;

public class MultiConsumerValueTaskNotifier<T>
{
    class WaiterNode : IValueTaskSource<T>
    {
        public CancellationTokenRegistration CancellationTokenRegistration { get; set; }
        ManualResetValueTaskSourceCore<T> core;

        public short Version { get; }

        public WaiterNode(short version)
        {
            Version = version;
            core.RunContinuationsAsynchronously = false;
        }

        public void SetResult(T result)
        {
            try
            {
                core.SetResult(result);
            }
            finally
            {
                CancellationTokenRegistration.Dispose();
            }
        }

        public void SetException(Exception exception)
        {
            try
            {
                core.SetException(exception);
            }
            finally
            {
                CancellationTokenRegistration.Dispose();
            }
        }

        public void SetCanceled()
        {
            SetException(new OperationCanceledException(CancellationTokenRegistration.Token));
        }

        T IValueTaskSource<T>.GetResult(short token)
        {
            try
            {
                return core.GetResult(token);
            }
            finally
            {
                CancellationTokenRegistration.Dispose();
            }
        }

        ValueTaskSourceStatus IValueTaskSource<T>.GetStatus(short token)
        {
            return core.GetStatus(token);
        }

        void IValueTaskSource<T>.OnCompleted(Action<object> continuation, object state, short token, ValueTaskSourceOnCompletedFlags flags)
        {
            core.OnCompleted(continuation, state, token, flags);
        }
    }

    readonly object gate = new();
    readonly List<WaiterNode> waiters = [];

    bool hasResult;
    T? result;
    Exception? exception;
    short version;

    public ValueTask<T> WaitAsync(CancellationToken cancellation)
    {
        lock (gate)
        {
            if (hasResult)
            {
                if (exception != null)
                {
                    return new ValueTask<T>(Task.FromException<T>(exception));
                }
                return new ValueTask<T>(result!);
            }
        }

        var node = new WaiterNode(version);
        if (cancellation.CanBeCanceled)
        {
            node.CancellationTokenRegistration = cancellation.UnsafeRegister(static state =>
            {
                ((WaiterNode)state!).SetCanceled();
            }, node);
        }

        lock (gate)
        {
            waiters.Add(node);
        }
        return new ValueTask<T>(node, 0);
    }

    public void Reset()
    {
        WaiterNode[] waiterToNotify = [];
        int count;

        lock (gate)
        {
            version++;
            exception = null;
            result = default;
            hasResult = false;
            count = waiters.Count;
            if (count > 0)
            {
                waiterToNotify = ArrayPool<WaiterNode>.Shared.Rent(count);
                waiters.CopyTo(waiterToNotify);
                waiters.Clear();
            }
        }

        for (var i = 0; i < count; i++)
        {
            waiterToNotify[i].SetException(new InvalidOperationException());
        }
        ArrayPool<WaiterNode>.Shared.Return(waiterToNotify);
    }

    public void SetResult(T result)
    {
        WaiterNode[] waiterToNotify = [];
        int waitersCount;
        short currentVersion;

        lock (gate)
        {
            hasResult = true;
            currentVersion = version;
            this.result = result;

            waitersCount = waiters.Count;
            if (waitersCount > 0)
            {
                waiterToNotify = ArrayPool<WaiterNode>.Shared.Rent(waitersCount);
                waiters.CopyTo(waiterToNotify);
                waiters.Clear();
            }
        }

        for (var i = 0; i < waitersCount; i++)
        {
            var waiter = waiterToNotify[i];
            if (waiter.Version != currentVersion)
            {
                waiter.SetException(new InvalidOperationException("invalid version"));
            }
            else
            {
                waiter.SetResult(result);
            }
        }
        ArrayPool<WaiterNode>.Shared.Return(waiterToNotify);
    }

    public void SetException(Exception exception)
    {
        WaiterNode[] waiterToNotify = [];
        int waitersCount;

        lock (gate)
        {
            hasResult = true;
            this.exception = exception;

            waitersCount = waiters.Count;
            if (waitersCount > 0)
            {
                waiterToNotify = ArrayPool<WaiterNode>.Shared.Rent(waitersCount);
                waiters.CopyTo(waiterToNotify);
                waiters.Clear();
            }
        }

        for (var i = 0; i < waitersCount; i++)
        {
            waiterToNotify[i].SetException(exception);
        }
        ArrayPool<WaiterNode>.Shared.Return(waiterToNotify);
    }

    void ValidateToken(short token)
    {
        if (version != token)
        {
            throw new InvalidOperationException("invalid token");
        }
    }
}