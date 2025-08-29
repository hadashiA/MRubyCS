namespace MRubyCS.Serializer.Tests;

[TestFixture]
public class GeneratedFormatterTest
{
    MRubyState state;

    [SetUp]
    public void Setup()
    {
        state = MRubyState.Create();
    }

    [Test]
    public void Serialize()
    {
        var result = MRubyValueSerializer.Serialize(new NestedFieldObject
        {
            IntField = 123,
            Y = 456,
            ArrayField = ["a", "bbb", "ccc"],
            DictField = new Dictionary<string, Struct1>
            {
                ["AAA"] = new() { Id = 99999 }
            }
        }, state);

        var props = result.As<RHash>();
        Assert.That(props[MRubyValue.From(state.Intern("int_field"u8))], Is.EqualTo(MRubyValue.From(123)));
        Assert.That(props[MRubyValue.From(state.Intern("alias_of_y"u8))], Is.EqualTo(MRubyValue.From(456)));

        var arrayField = props[MRubyValue.From(state.Intern("array_field"u8))].As<RArray>();
        Assert.That(arrayField.Length, Is.EqualTo(3));
        Assert.That(state.ValueEquals(arrayField[0], MRubyValue.From(state.NewString("a"u8))), Is.True);
        Assert.That(state.ValueEquals(arrayField[1], MRubyValue.From(state.NewString("bbb"u8))), Is.True);
        Assert.That(state.ValueEquals(arrayField[2], MRubyValue.From(state.NewString("ccc"u8))), Is.True);

        var dictField = props[MRubyValue.From(state.Intern("dict_field"u8))].As<RHash>();
        Assert.That(dictField.Length, Is.EqualTo(1));

        var nestedProps = dictField[MRubyValue.From(state.NewString("AAA"u8))].As<RHash>();
        Assert.That(nestedProps[MRubyValue.From(state.Intern("id"u8))],  Is.EqualTo(MRubyValue.From(99999)));
    }

    [Test]
    public void Deserialize()
    {
        var props = state.NewHash();

        props.Add(
            MRubyValue.From(state.Intern("int_field"u8)),
            MRubyValue.From(12345));

        var array = state.NewArray();
        array.Push(MRubyValue.From(state.NewString("aaa"u8)));
        array.Push(MRubyValue.From(state.NewString("bbb"u8)));
        array.Push(MRubyValue.From(state.NewString("ccc"u8)));

        props.Add(
            MRubyValue.From(state.Intern("array_field"u8)),
            MRubyValue.From(array));

        var hash = state.NewHash();

        var nestedProps = state.NewHash();
        nestedProps.Add(
            MRubyValue.From(state.Intern("id"u8)),
            MRubyValue.From(99999));

        hash.Add(
            MRubyValue.From(state.Intern("Hoge"u8)),
            MRubyValue.From(nestedProps));

        props.Add(
            MRubyValue.From(state.Intern("dict_field"u8)),
            MRubyValue.From(hash));


        var result = MRubyValueSerializer.Deserialize<NestedFieldObject>(
            MRubyValue.From(props),
            state)!;

        Assert.That(result.IntField, Is.EqualTo(12345));
        Assert.That(result.ArrayField.Length, Is.EqualTo(3));
        Assert.That(result.ArrayField[0], Is.EqualTo("aaa"));
        Assert.That(result.ArrayField[1], Is.EqualTo("bbb"));
        Assert.That(result.ArrayField[2], Is.EqualTo("ccc"));
        Assert.That(result.DictField.Count, Is.EqualTo(1));
        Assert.That(result.DictField["Hoge"].Id, Is.EqualTo(99999));
    }

    [Test]
    public void DeserializeWithCtor()
    {
        var props = state.NewHash();
        props.Add(MRubyValue.From(state.Intern("X"u8)), MRubyValue.From(123));
        props.Add(MRubyValue.From(state.Intern("Y"u8)), MRubyValue.From(456));
        props.Add(MRubyValue.From(state.Intern("Hoge"u8)), MRubyValue.From(state.NewString("hello hello"u8)));

        var result = MRubyValueSerializer.Deserialize<MRubyConstructorClass>(MRubyValue.From(props), state)!;
        Assert.That(result.X, Is.EqualTo(123));
        Assert.That(result.Y, Is.EqualTo(456));
        Assert.That(result.Hoge, Is.EqualTo("hello hello"));
    }
}
