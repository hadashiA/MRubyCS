namespace MRubyCS.Serializer.Tests;

[TestFixture]
public class MRubyValueToMRubyValueTest
{
    MRubyState mrb;

    [SetUp]
    public void Setup()
    {
        mrb = MRubyState.Create();
    }

    [Test]
    public void Serialize()
    {
        var value = mrb.Intern("abcde");
        var result = MRubyValueSerializer.Serialize(value, mrb);
        Assert.That(result, Is.EqualTo(new MRubyValue(value)));
    }
}