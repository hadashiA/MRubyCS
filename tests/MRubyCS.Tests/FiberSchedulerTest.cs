using System.Diagnostics;
using MRubyCS.Compiler;

namespace MRubyCS.Tests;

[TestFixture]
public class FiberSchedulerTest
{
    MRubyState mrb = default!;
    MRubyCompiler compiler = default!;

    [SetUp]
    public void Before()
    {
        mrb = MRubyState.Create();
        compiler = MRubyCompiler.Create(mrb);
    }

    [TearDown]
    public void After()
    {
        compiler.Dispose();
        mrb.Dispose();
    }

    [Test]
    public async Task Sleep_InsideFiber_YieldsToScheduler()
    {
        mrb.SetFiberScheduler(new ThreadPoolFiberScheduler());

        var code = """
                   sleep 0.05
                   :done
                   """u8;

        var fiber = compiler.LoadSourceCodeAsFiber(code);

        var sw = Stopwatch.StartNew();
        fiber.Resume();
        await fiber.WaitForTerminateAsync();
        sw.Stop();

        Assert.That(fiber.IsAlive, Is.False);
        Assert.That(sw.Elapsed.TotalSeconds, Is.GreaterThanOrEqualTo(0.04));
    }

    [Test]
    public async Task Sleep_InsideFiber_MultipleSleeps()
    {
        mrb.SetFiberScheduler(new ThreadPoolFiberScheduler());

        var code = """
                   3.times { sleep 0.02 }
                   :done
                   """u8;

        var fiber = compiler.LoadSourceCodeAsFiber(code);

        fiber.Resume();
        await fiber.WaitForTerminateAsync();

        Assert.That(fiber.IsAlive, Is.False);
    }

    [Test]
    public void Sleep_AtRoot_BlocksHostThread()
    {
        // sleep called at the root fiber falls back to Thread.Sleep
        // because there is no caller to yield to.
        var code = """
                   sleep 0.02
                   :done
                   """u8;

        var sw = Stopwatch.StartNew();
        var result = compiler.LoadSourceCode(code);
        sw.Stop();

        Assert.That(result.SymbolValue, Is.EqualTo(mrb.Intern("done"u8)));
        Assert.That(sw.Elapsed.TotalSeconds, Is.GreaterThanOrEqualTo(0.015));
    }

    [Test]
    public void Sleep_AtRootEvenWithScheduler_StillSynchronous()
    {
        // The scheduler is installed but the call site is the root fiber.
        // Sleep must fall back to Thread.Sleep; the scheduler hook must
        // not be invoked.
        var spy = new SpyScheduler();
        mrb.SetFiberScheduler(spy);

        var code = """
                   sleep 0.02
                   :done
                   """u8;

        var sw = Stopwatch.StartNew();
        compiler.LoadSourceCode(code);
        sw.Stop();

        Assert.That(spy.SleepCallCount, Is.EqualTo(0));
        Assert.That(sw.Elapsed.TotalSeconds, Is.GreaterThanOrEqualTo(0.015));
    }

    [Test]
    public async Task SyncContextScheduler_Sleep_RoundTrip()
    {
        // SynchronizationContextFiberScheduler with a one-thread pumped
        // context. Verifies that scheduler hooks route resume callbacks
        // through SynchronizationContext.Post.
        using var ctx = new PumpedSyncContext();
        using var scheduler = new SynchronizationContextFiberScheduler(ctx);
        mrb.SetFiberScheduler(scheduler);

        var fiber = compiler.LoadSourceCodeAsFiber("sleep 0.02; :done"u8);

        // Real hosts have SynchronizationContext.Current == captured ctx
        // on the VM thread; mirror that here so scheduler-hook awaits
        // capture our pumped context.
        ctx.Run(() => fiber.Resume());

        await ctx.PumpUntilAsync(() => !fiber.IsAlive, TimeSpan.FromSeconds(2));

        Assert.That(fiber.IsAlive, Is.False);
        Assert.That(ctx.PostCount, Is.GreaterThan(0), "Post should have been invoked");
    }

    /// <summary>Minimal pumped SyncContext for the test above.</summary>
    sealed class PumpedSyncContext : SynchronizationContext, IDisposable
    {
        readonly System.Collections.Concurrent.BlockingCollection<(SendOrPostCallback cb, object? state)> queue = new();
        public int PostCount;

        public override void Post(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref PostCount);
            queue.Add((d, state));
        }

        public void Run(Action body)
        {
            var prev = Current;
            SetSynchronizationContext(this);
            try { body(); }
            finally { SetSynchronizationContext(prev); }
        }

        public async Task PumpUntilAsync(Func<bool> condition, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (!condition() && DateTime.UtcNow < deadline)
            {
                if (queue.TryTake(out var item, 50))
                {
                    // Mirror real sync contexts: set Current=self while
                    // invoking the callback so its inner awaits capture us.
                    var prev = Current;
                    SetSynchronizationContext(this);
                    try { item.cb(item.state); }
                    finally { SetSynchronizationContext(prev); }
                }
                else
                {
                    await Task.Yield();
                }
            }
        }

        public void Dispose() => queue.Dispose();
    }

    [Test]
    public async Task ThreadPass_YieldsToScheduler()
    {
        // Thread.pass must hit the scheduler's Yield hook and resume on
        // the next tick. With ThreadPoolFiberScheduler the resume hops
        // through the threadpool, so a counter incremented before+after
        // observes the yield round-trip.
        mrb.SetFiberScheduler(new ThreadPoolFiberScheduler());

        var code = """
                   x = 0
                   Thread.pass
                   x = 1
                   x
                   """u8;

        var fiber = compiler.LoadSourceCodeAsFiber(code);
        fiber.Resume();
        var result = await fiber.WaitForTerminateAsync();

        Assert.That(fiber.IsAlive, Is.False);
        Assert.That(result.IntegerValue, Is.EqualTo(1));
    }

    [Test]
    public async Task SleepZero_RoutesToYield()
    {
        // sleep 0 must NOT call KernelSleep — it routes to Yield to avoid
        // an unnecessary Timer allocation.
        var spy = new TrackingScheduler();
        mrb.SetFiberScheduler(spy);

        var fiber = compiler.LoadSourceCodeAsFiber("sleep 0; :done"u8);
        fiber.Resume();
        await fiber.WaitForTerminateAsync();

        Assert.That(spy.KernelSleepCalls, Is.EqualTo(0));
        Assert.That(spy.YieldCalls, Is.EqualTo(1));
    }

    sealed class TrackingScheduler : IMRubyFiberScheduler
    {
        public int KernelSleepCalls;
        public int YieldCalls;

        public void KernelSleep(TimeSpan duration, RFiber fiber, CancellationToken cancellationToken = default)
        {
            KernelSleepCalls++;
            // We still have to drive the fiber to completion or the test
            // hangs. Resume on threadpool to mimic ThreadPoolFiberScheduler.
            ThreadPool.UnsafeQueueUserWorkItem(static state => ((RFiber)state!).Resume(), fiber);
            fiber.Yield();
        }
        public void Block(RFiber fiber, CancellationToken cancellationToken = default) { }
        public void Unblock(RFiber fiber, MRubyValue resumeValue = default) { }
        public void Yield(RFiber fiber, CancellationToken cancellationToken = default)
        {
            YieldCalls++;
            ThreadPool.UnsafeQueueUserWorkItem(static state => ((RFiber)state!).Resume(), fiber);
            fiber.Yield();
        }
        public void ReadStream<TState>(System.IO.Stream stream, Memory<byte> buffer, TState state, Func<int, TState, MRubyValue> projection, RFiber fiber, CancellationToken cancellationToken = default, bool disposeStream = false) { }
        public void ReadStreamToEnd<TState>(System.IO.Stream stream, System.Buffers.IBufferWriter<byte> writer, TState state, Func<long, TState, MRubyValue> projection, RFiber fiber, CancellationToken cancellationToken = default, bool disposeStream = false) { }
        public void WriteStream(System.IO.Stream stream, ReadOnlyMemory<byte> data, RFiber fiber, CancellationToken cancellationToken = default, bool disposeStream = false) { }
        public void Dispose() { }
    }

    [Test]
    public void ExistingFiberSemantics_Unchanged()
    {
        // Regression: the scheduler hook is invisible to ordinary
        // Resume/Yield flow. This mirrors FiberTest.Resume.
        var code = """
                   Fiber.new do |x|
                     Fiber.yield(x * 2)
                     Fiber.yield(x * 3)
                   end
                   """u8;

        var fiber = compiler.LoadSourceCode(code).As<RFiber>();

        Assert.That(fiber.Resume(100).IntegerValue, Is.EqualTo(200));
        Assert.That(fiber.Resume(100).IntegerValue, Is.EqualTo(300));
        // After the second yield resumes, the block ends with that resumed
        // value as its last expression -> Fiber#resume returns it.
        Assert.That(fiber.Resume(100).IntegerValue, Is.EqualTo(100));
        Assert.That(fiber.IsAlive, Is.False);
    }

    [Test]
    public async Task Resume_From_ThreadPool_Delivers_Value_To_Ruby()
    {
        // Same as the next test but Resume(:hello) is called from a
        // thread-pool task instead of the test thread. This isolates
        // whether the resume-value flow has thread-affinity issues.
        mrb.SetFiberScheduler(new ThreadPoolFiberScheduler());

        var helloSym = mrb.Intern("hello"u8);
        mrb.DefineMethod(mrb.KernelModule, mrb.Intern("yield_only"u8),
            new MRubyMethod((state, _) =>
            {
                state.CurrentFiber.Yield();
                return MRubyValue.Nil;
            }));

        var fiber = compiler.LoadSourceCodeAsFiber("yield_only"u8);
        fiber.Resume(); // first run -> hits Yield
        // From thread pool: do the second Resume(:hello).
        var second = await Task.Run(() => fiber.Resume(new MRubyValue(helloSym)));
        TestContext.Out.WriteLine($"second VType={second.VType} sym={(second.IsSymbol ? mrb.NameOf(second.SymbolValue).ToString() : "?")}");
        Assert.That(fiber.IsAlive, Is.False);
        Assert.That(second.SymbolValue, Is.EqualTo(helloSym));
    }

    [Test]
    public void Resume_With_Argument_Delivers_Value_To_Ruby_Through_Fiber_Yield()
    {
        // Direct test of the VM-level resume-value contract for top-level
        // programs. Calls fiber.Yield from inside a C# method, then the
        // test thread directly Resume(value)'s the fiber. The value
        // should land in Ruby (the C# method's apparent return).
        mrb.SetFiberScheduler(new ThreadPoolFiberScheduler());

        RFiber fiberRef = null!;
        var helloSym = mrb.Intern("hello"u8);
        mrb.DefineMethod(mrb.KernelModule, mrb.Intern("yield_then_resume"u8),
            new MRubyMethod((state, _) =>
            {
                fiberRef = state.CurrentFiber;
                fiberRef.Yield();
                return MRubyValue.Nil;
            }));

        var fiber = compiler.LoadSourceCodeAsFiber("yield_then_resume"u8);
        var first = fiber.Resume();
        // Explicit second resume from test thread (no scheduler involvement)
        var second = fiber.Resume(new MRubyValue(helloSym));
        TestContext.Out.WriteLine($"first VType={first.VType} sym={(first.IsSymbol ? mrb.NameOf(first.SymbolValue).ToString() : "?")}");
        TestContext.Out.WriteLine($"second VType={second.VType} sym={(second.IsSymbol ? mrb.NameOf(second.SymbolValue).ToString() : "?")}");
        Assert.That(fiber.IsAlive, Is.False);
        // The value Resume(:hello) passed in should be the program's
        // final value.
        Assert.That(second.SymbolValue, Is.EqualTo(helloSym));
    }

    [Test]
    public async Task Block_Unblock_DeliversResumeValueToRuby()
    {
        // End-to-end: scheduler.Unblock(blocker, fiber, value) delivers
        // value into Ruby as the apparent return of the C# method that
        // invoked Block. Block yields internally (CRuby-style), so the
        // caller arranges the Unblock task *before* calling Block.
        // Captured via a Ruby-side helper (record) so we observe what the
        // running program actually sees.
        mrb.SetFiberScheduler(new ThreadPoolFiberScheduler());

        var helloSym = mrb.Intern("hello"u8);
        var capturedRuby = new TaskCompletionSource<MRubyValue>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        mrb.DefineMethod(mrb.KernelModule, mrb.Intern("await_value"u8),
            new MRubyMethod((state, self) =>
            {
                var fiber = state.CurrentFiber;
                var sched = state.FiberScheduler!;
                _ = Task.Run(async () =>
                {
                    await Task.Delay(20);
                    sched.Unblock(fiber, new MRubyValue(helloSym));
                });
                sched.Block(fiber);
                return MRubyValue.Nil;
            }));
        mrb.DefineMethod(mrb.KernelModule, mrb.Intern("record"u8),
            new MRubyMethod((state, _) =>
            {
                capturedRuby.TrySetResult(state.GetArgumentAt(0));
                return MRubyValue.Nil;
            }));

        var fiber = compiler.LoadSourceCodeAsFiber("""
            v = await_value
            record(v)
            """u8);
        fiber.Resume();

        var captured = await capturedRuby.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(captured.IsSymbol, Is.True);
        Assert.That(captured.SymbolValue, Is.EqualTo(helloSym));
    }

    [Test]
    public async Task Block_Cancelled_ResumesEvenWithoutUnblock()
    {
        // When Block has a finite timeout and Unblock never arrives, the
        // continuation must still run and Resume the fiber (with Nil).
        // This guards against the regression where ContinueWith was
        // cancelled along with the fiber-cancellation token, leaving the
        // fiber permanently parked.
        mrb.SetFiberScheduler(new ThreadPoolFiberScheduler());

        mrb.DefineMethod(mrb.KernelModule, mrb.Intern("await_value"u8),
            new MRubyMethod((state, self) =>
            {
                var fiber = state.CurrentFiber;
                var sched = state.FiberScheduler!;
                var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
                sched.Block(fiber, cts.Token);
                return MRubyValue.Nil;
            }));

        var code = "await_value; :done"u8;
        var fiber = compiler.LoadSourceCodeAsFiber(code);
        fiber.Resume();

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (fiber.IsAlive && DateTime.UtcNow < deadline)
        {
            await Task.Delay(5);
        }
        Assert.That(fiber.IsAlive, Is.False, "fiber should resume after timeout (not stay parked)");
    }

    [Test]
    public async Task PendingException_IsCatchableByRubyRescue()
    {
        // The scheduler delivers an exception by setting PendingException
        // before Resume. The fiber wraps the parking call in begin/rescue
        // and must observe a normal Ruby raise that the rescue clause
        // catches — not a fiber-killing unwind.
        mrb.SetFiberScheduler(new ThreadPoolFiberScheduler());

        mrb.DefineMethod(mrb.KernelModule, mrb.Intern("await_boom"u8),
            new MRubyMethod((state, self) =>
            {
                var fiber = state.CurrentFiber;
                Task.Run(() =>
                {
                    fiber.PendingException = new RException(
                        state.NewString("boom"u8),
                        state.GetExceptionClass(Names.RuntimeError));
                    fiber.Resume();
                });
                fiber.Yield();
                return MRubyValue.Nil;
            }));

        var code = """
                   begin
                     await_boom
                     :no_raise
                   rescue => e
                     e.message
                   end
                   """u8;

        var fiber = compiler.LoadSourceCodeAsFiber(code);
        fiber.Resume();
        var result = await fiber.WaitForTerminateAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.That(fiber.IsAlive, Is.False);
        Assert.That(result.VType, Is.EqualTo(MRubyVType.String), $"expected rescue to catch and return e.message; got {result.VType}");
        Assert.That(result.As<RString>().AsSpan().SequenceEqual("boom"u8), Is.True);
    }

    [Test]
    public void Block_TwiceForSameFiber_Throws()
    {
        // Re-blocking a fiber that's already parked would orphan the previous
        // BlockEntry (its TCS would never resolve), so the scheduler must
        // reject the second registration instead of silently overwriting.
        using var scheduler = new ThreadPoolFiberScheduler();
        mrb.SetFiberScheduler(scheduler);

        Exception? caught = null;
        mrb.DefineMethod(mrb.KernelModule, mrb.Intern("park_twice"u8),
            new MRubyMethod((state, self) =>
            {
                var fiber = state.CurrentFiber;
                var sched = state.FiberScheduler!;
                sched.Block(fiber);
                // Unreachable on the normal path (Block yields), but if the
                // guard misfires we still want a deterministic failure.
                try { sched.Block(fiber); }
                catch (InvalidOperationException ex) { caught = ex; }
                return MRubyValue.Nil;
            }));

        // Drive the second Block from outside the fiber: after the fiber
        // yields on the first Block, call Block again on the same fiber.
        var fiber = compiler.LoadSourceCodeAsFiber("park_twice"u8);
        fiber.Resume();

        Assert.Throws<InvalidOperationException>(() => scheduler.Block(fiber));
    }

    [Test]
    public void KernelSleep_TwiceForSameFiber_Throws()
    {
        using var scheduler = new ThreadPoolFiberScheduler();
        mrb.SetFiberScheduler(scheduler);

        var fiber = compiler.LoadSourceCodeAsFiber("sleep 10; :done"u8);
        fiber.Resume();
        // fiber is now parked in KernelSleep. A second KernelSleep on the
        // same fiber must throw rather than overwrite the timer.
        Assert.Throws<InvalidOperationException>(
            () => scheduler.KernelSleep(TimeSpan.FromSeconds(1), fiber));
    }

    sealed class SpyScheduler : IMRubyFiberScheduler
    {
        public int SleepCallCount;

        public void KernelSleep(TimeSpan duration, RFiber fiber, CancellationToken cancellationToken = default)
            => SleepCallCount++;

        public void Block(RFiber fiber, CancellationToken cancellationToken = default) { }
        public void Unblock(RFiber fiber, MRubyValue resumeValue = default) { }
        public void Yield(RFiber fiber, CancellationToken cancellationToken = default) { }
        public void ReadStream<TState>(System.IO.Stream stream, Memory<byte> buffer, TState state, Func<int, TState, MRubyValue> projection, RFiber fiber, CancellationToken cancellationToken = default, bool disposeStream = false) { }
        public void ReadStreamToEnd<TState>(System.IO.Stream stream, System.Buffers.IBufferWriter<byte> writer, TState state, Func<long, TState, MRubyValue> projection, RFiber fiber, CancellationToken cancellationToken = default, bool disposeStream = false) { }
        public void WriteStream(System.IO.Stream stream, ReadOnlyMemory<byte> data, RFiber fiber, CancellationToken cancellationToken = default, bool disposeStream = false) { }
        public void Dispose() { }
    }
}
