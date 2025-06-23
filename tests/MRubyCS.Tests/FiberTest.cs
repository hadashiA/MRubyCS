using MRubyCS.Compiler;

namespace MRubyCS.Tests;

[TestFixture]
public class FiberTest
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
    }

    [Test]
    public void Resume()
    {
        var code = """
                   Fiber.new do |x|
                     Fiber.yield(x * 2)
                     Fiber.yield(x * 3)
                   end
                   """u8;

        var fiber = compiler.LoadAsFiber(code);

        var result1 = fiber.Resume(MRubyValue.From(100));
        Assert.That(result1.IntegerValue, Is.EqualTo(200));
        Assert.That(fiber.IsAlive, Is.True);

        var result2 = fiber.Resume(MRubyValue.From(100));
        Assert.That(result2.IntegerValue, Is.EqualTo(300));
        Assert.That(fiber.IsAlive, Is.True);

        var result3 = fiber.Resume(MRubyValue.From(100));
        Assert.That(result3.IsNil, Is.True);
        Assert.That(fiber.IsAlive, Is.False);
    }

    [Test]
    public async Task RunAsync()
    {
        var code = """
                   Fiber.new do |x|
                     Fiber.yield
                     Fiber.yield
                   end
                   """u8;

        var fiber = compiler.LoadAsFiber(code);
        Assert.That(fiber.IsAlive, Is.True);

        var run = fiber.RunAsync();
        fiber.Resume();
        fiber.Resume();
        await run;

        Assert.That(fiber.IsAlive, Is.False);
    }

    [Test]
    public async Task WaitForResumeAsync()
    {
        var code = """
                   Fiber.new do |x|
                     Fiber.yield
                     Fiber.yield
                   end
                   """u8;

        var fiber = compiler.LoadAsFiber(code);

        var wait1 = fiber.WaitForResumeAsync();

        Assert.That(wait1.IsCompleted, Is.False);

        fiber.Resume();
        await wait1;
        Assert.That(wait1.IsCompleted, Is.True);

        var wait2 = fiber.WaitForResumeAsync();
        Assert.That(wait2.IsCompleted, Is.False);
        fiber.Resume();
        await wait2;
        Assert.That(wait2.IsCompleted, Is.True);
    }

    // [Test]
    // public async Task WaitForResumeAsync_MultipleConsumers()
    // {
    //     var code = """
    //                Fiber.new do |x|
    //                  Fiber.yield(x * 2)
    //                  Fiber.yield(x * 3)
    //                  x * 4
    //                end
    //                """u8;
    //
    //     var fiber = compiler.LoadAsFiber(code);
    //
    //     var consumer1Results = new List<MRubyValue>();
    //     var consumer2Results = new List<MRubyValue>();
    //     var consumer3Results = new List<MRubyValue>();
    //
    //     var consumer1 = Task.Run(async () =>
    //     {
    //         while (fiber.IsAlive)
    //         {
    //             try
    //             {
    //                 var result = await fiber.WaitForResumeAsync();
    //                 consumer1Results.Add(result);
    //             }
    //             catch (InvalidOperationException)
    //             {
    //                 // Fiber終了
    //                 break;
    //             }
    //         }
    //     });
    //
    //     var consumer2 = Task.Run(async () =>
    //     {
    //         while (fiber.IsAlive)
    //         {
    //             try
    //             {
    //                 var result = await fiber.WaitForResumeAsync();
    //                 consumer2Results.Add(result);
    //             }
    //             catch (InvalidOperationException)
    //             {
    //                 // Fiber終了
    //                 break;
    //             }
    //         }
    //     });
    //
    //     var consumer3 = Task.Run(async () =>
    //     {
    //         while (fiber.IsAlive)
    //         {
    //             try
    //             {
    //                 var result = await fiber.WaitForResumeAsync();
    //                 consumer3Results.Add(result);
    //             }
    //             catch (InvalidOperationException)
    //             {
    //                 // Fiber終了
    //                 break;
    //             }
    //         }
    //     });
    //
    //     await Task.Delay(100);
    //
    //     var result1 = fiber.Resume(MRubyValue.From(10));
    //     Assert.That(result1.IntegerValue, Is.EqualTo(20));
    //
    //     await Task.Delay(100);
    //
    //     var result2 = fiber.Resume(MRubyValue.From(10));
    //     Assert.That(result2.IntegerValue, Is.EqualTo(30));
    //
    //     await Task.Delay(100);
    //
    //     var result3 = fiber.Resume(MRubyValue.From(10));
    //     Assert.That(result3.IntegerValue, Is.EqualTo(40));
    //
    //     await Task.WhenAll(consumer1, consumer2, consumer3);
    //
    //     Assert.That(consumer1Results.Count, Is.EqualTo(3));
    //     Assert.That(consumer2Results.Count, Is.EqualTo(3));
    //     Assert.That(consumer3Results.Count, Is.EqualTo(3));
    //
    //     foreach (var results in new[] { consumer1Results, consumer2Results, consumer3Results })
    //     {
    //         Assert.That(results[0].IntegerValue, Is.EqualTo(20));
    //         Assert.That(results[1].IntegerValue, Is.EqualTo(30));
    //         Assert.That(results[2].IntegerValue, Is.EqualTo(40));
    //     }
    // }
}