using System;
using System.Collections.Generic;

namespace MRubyCS.Serializer;

public class EnumAsStringFormatter<T> : IMRubyValueFormatter<T> where T : Enum
{
    static readonly Dictionary<string, T> Values;

    static EnumAsStringFormatter()
    {
        var names = Enum.GetNames(typeof(T));
        var values = (T[])Enum.GetValues(typeof(T));

        Values = new Dictionary<string, T>(names.Length);
        for (var i = 0; i < names.Length; ++i)
        {
            Values.Add(names[i], values[i]);
        }
    }

    // public T? Deserialize(MrbValue mrbValue,MRubyContext context, MrbValueSerializerOptions options)
    // {
    //     var str = mrbValue.ToString(context);
    //     if (Values.TryGetValue(str, out var value))
    //     {
    //         return value;
    //     }
    //     throw new MRubySerializationException($"Unknown enum value: {str} in {typeof(T)}");
    // }

    public MRubyValue Serialize(T value, MRubyState state, MRubyValueSerializerOptions options)
    {
        throw new NotImplementedException();
    }

    public T Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        throw new NotImplementedException();
    }
}