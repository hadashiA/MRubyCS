using System.Text;
using MRubyCS.Compiler;

namespace MRubyCS.Tests;

[TestFixture]
public class ExceptionBacktraceTest
{
    MRubyState mrb = default!;
    MRubyCompiler compiler = default!;

    [SetUp]
    public void BeforeEach()
    {
        mrb = MRubyState.Create();
        compiler = MRubyCompiler.Create(mrb);
    }

    [TearDown]
    public void AfterEach()
    {
        compiler.Dispose();
        mrb.Dispose();
    }

    MRubyRaiseException Run(byte[] src, string filename)
    {
        try
        {
            using var c = compiler.Compile(src, filename: filename);
            mrb.LoadBytecode(c.AsBytecode());
        }
        catch (MRubyRaiseException ex)
        {
            return ex;
        }
        Assert.Fail("expected MRubyRaiseException");
        return null!;
    }

    [Test]
    public void RaiseWithStringMessage_AttachesBacktrace_WithFileLine()
    {
        var ex = Run(Encoding.UTF8.GetBytes("""
            def deep
              raise "boom"
            end
            deep
            """), "test.rb");
        Assert.That(ex.ExceptionObject.Backtrace, Is.Not.Null);

        var text = ex.ExceptionObject.Backtrace!.ToString(mrb);
        TestContext.Out.WriteLine(text);
        Assert.That(text, Does.Contain("test.rb"));
        Assert.That(text, Does.Contain(":2"), "raise is on source line 2");
    }

    [Test]
    public void RaiseWithExceptionObject_AlsoAttachesBacktrace()
    {
        // `raise SomeError.new("msg")` previously went through Raise(RException)
        // without capturing a backtrace. We now capture lazily if absent.
        var ex = Run(Encoding.UTF8.GetBytes("""
            raise RuntimeError.new("boom")
            """), "obj.rb");
        Assert.That(ex.ExceptionObject.Backtrace, Is.Not.Null);
        var text = ex.ExceptionObject.Backtrace!.ToString(mrb);
        TestContext.Out.WriteLine(text);
        Assert.That(text, Does.Contain("obj.rb"));
    }

    [Test]
    public void BacktraceIncludesCallSiteLine_NotMerelyProcStart()
    {
        // Pre-existing bug: Backtrace.Capture used proc.ProgramCounter (always the
        // proc's starting pc) instead of callInfo.ProgramCounter. As a result every
        // frame reported line 1. This test pins down the fix.
        var ex = Run(Encoding.UTF8.GetBytes("""
            def a
              b
            end
            def b
              raise "x"
            end
            a
            """), "chain.rb");
        var text = ex.ExceptionObject.Backtrace!.ToString(mrb);
        TestContext.Out.WriteLine(text);
        Assert.That(text, Does.Contain(":5"), "raise is on source line 5");
    }
}
