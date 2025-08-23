using System;

namespace MRubyCS.Serializer;

public class NullableStringFormatter : IMRubyValueFormatter<string?>
{
    public static readonly NullableStringFormatter Instance = new();
    public MRubyValue Serialize(string? value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value == null)
        {
            return default;
        }
        return MRubyValue.From(state.NewString($"{value}"));
    }

    public string? Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value.IsNil)
        {
            return null;
        }
        return state.Stringify(value).ToString();
    }
}

public class ByteArrayFormatter : IMRubyValueFormatter<byte[]?>
{
    public static readonly ByteArrayFormatter Instance = new();

    public MRubyValue Serialize(byte[]? value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value == null) return default;

        var str = state.NewString(value.AsSpan());
        return MRubyValue.From(str);
    }

    public byte[]? Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value.IsNil) return null;

        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.String);
        return value.As<RString>().AsSpan().ToArray();
    }
}
