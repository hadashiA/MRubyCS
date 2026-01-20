using MRubyCS.Compiler;

namespace MRubyCS.Tests;

[TestFixture]
public class RiteParserTest
{
    [Test]
    public void Parse()
    {
        var mrb = MRubyState.Create();
        using var compiler = MRubyCompiler.Create(mrb);
        using var compilation = compiler.Compile("a = 'abcdefg'"u8);
        var irep = compilation.ToIrep(mrb);
        Assert.That(irep.PoolValues.Count, Is.EqualTo(1));
    }
}
