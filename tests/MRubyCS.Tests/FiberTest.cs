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

        var irep = compiler.Compile(code);
        var fiber = mrb.CreateFiber(irep);

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
    public void WaitForResumeAsync()
    {
        var code = """
                   Fiber.new do |x|
                     Fiber.yield
                     Fiber.yield
                   end
                   """u8;

        var irep = compiler.Compile(code);
        var fiber = mrb.CreateFiber(irep);

        var count = 0;
        var completed = false;
        Task.Run(async () =>
        {
            while (fiber.IsAlive)
            {
                await fiber.WaitForResumeAsync();
                count++;
            }

            completed = true;
        });

        Assert.That(count, Is.EqualTo(0));

        fiber.Resume();
        Assert.That(count, Is.EqualTo(1));

        fiber.Resume();
        Assert.That(count, Is.EqualTo(2));

        fiber.Resume();
        Assert.That(completed, Is.True);
    }
}