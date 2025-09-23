namespace MRubyCS.Serializer.Tests;

[TestFixture]
public class EnumAsStringFormatterTest
{
    enum TestEnum
    {
        Hoge,
        BarBuz,
    }

    MRubyState mrb = default!;

    [SetUp]
    public void SetUp()
    {
        mrb = MRubyState.Create();
    }

    [Test]
    public void Serialize()
    {
        var result1 = MRubyValueSerializer.Serialize(TestEnum.Hoge, mrb);
        Assert.That(result1.IsSymbol, Is.True);
        Assert.That(result1.SymbolValue, Is.EqualTo(mrb.Intern("hoge"u8)));

        var result2 = MRubyValueSerializer.Serialize(TestEnum.BarBuz, mrb);
        Assert.That(result2.IsSymbol, Is.True);
        Assert.That(result2.SymbolValue, Is.EqualTo(mrb.Intern("bar_buz"u8)));
    }

    [Test]
    public void Deserialize()
    {
        var result1 = MRubyValueSerializer.Deserialize<TestEnum>(mrb.Intern("hoge"), mrb);
        Assert.That(result1, Is.EqualTo(TestEnum.Hoge));

        var result2 = MRubyValueSerializer.Deserialize<TestEnum>(mrb.Intern("bar_buz"), mrb);
        Assert.That(result2, Is.EqualTo(TestEnum.BarBuz));

        Assert.Throws<MRubySerializationException>(() => MRubyValueSerializer.Deserialize<TestEnum>(1, mrb));
    }
}