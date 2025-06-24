using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace MRubyCS.Internals;

public class MultiConsumerValueTaskNotifier<T>
{
    class WaiterNode : IValueTaskSource<T>
    {
        static readonly ConcurrentQueue<WaiterNode> Pool = new();

        public static WaiterNode Rent(short tag)
        {
            if (!Pool.TryDequeue(out var node))
            {
                node = new WaiterNode();
            }
            node.Tag = tag;
            return node;
        }

        public CancellationTokenRegistration CancellationTokenRegistration { get; set; }
        ManualResetValueTaskSourceCore<T> core;

        public short Tag { get; private set; }
        public short Version => core.Version;

        WaiterNode()
        {
            core.RunContinuationsAsynchronously = false;
        }

        public void SetResult(T result)
        {
            core.SetResult(result);
        }

        public void SetException(Exception exception)
        {
            core.SetException(exception);
        }

        public void SetCanceled()
        {
            SetException(new OperationCanceledException(CancellationTokenRegistration.Token));
        }

        void Return()
        {
            CancellationTokenRegistration.Dispose();
            core.Reset();
            Pool.Enqueue(this);
        }

        T IValueTaskSource<T>.GetResult(short token)
        {
            try
            {
                return core.GetResult(token);
            }
            finally
            {
                Return();
            }
        }

        ValueTaskSourceStatus IValueTaskSource<T>.GetStatus(short token)
        {
            return core.GetStatus(token);
        }

        void IValueTaskSource<T>.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
        {
            core.OnCompleted(continuation, state, token, flags);
        }
    }

    readonly object gate = new();
    readonly List<WaiterNode> waiters = [];

    bool hasResult;
    T? result;
    Exception? error;
    short version;

    public ValueTask<T> WaitAsync(CancellationToken cancellation)
    {
        var node = WaiterNode.Rent(version);
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
        return new ValueTask<T>(node, node.Version);
    }

    public void Reset()
    {
        WaiterNode[] waiterToNotify = [];
        int count;
        bool currentResultExists;
        int currentVersion;
        T? currentResult;
        Exception? currentError;

        lock (gate)
        {
            currentResultExists = this.hasResult;
            currentResult = this.result;
            currentError = this.error;

            this.hasResult = false;
            this.result = default;
            this.error = null;

            count = waiters.Count;
            currentVersion = version;
            unchecked { version++; }

            if (count > 0)
            {
                waiterToNotify = ArrayPool<WaiterNode>.Shared.Rent(count);
                waiters.CopyTo(waiterToNotify);
                waiters.Clear();
            }
        }

        for (var i = 0; i < count; i++)
        {
            var waiter = waiterToNotify[i];
            if (currentResultExists)
            {
                if (currentError is not null)
                {
                    waiter.SetException(currentError);
                }
                else
                {
                    waiter.SetResult(currentResult!);
                }
            }
            else
            {
                waiter.SetException(new InvalidOperationException("source was reset before await."));
            }
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
            currentVersion = version;
            this.hasResult = true;
            this.result = result;
            this.error = null;

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
            waiter.SetResult(result);
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
            this.error = exception;
            this.result = default;
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
}