using System;
using System.Collections;
using System.Collections.Generic;

namespace MRubyCS.Serializer;

public class PrimitiveObjectFormatter : IMRubyValueFormatter<object?>
{
    public static readonly PrimitiveObjectFormatter Instance = new();

    static readonly Dictionary<Type, int> TypeToJumpCode = new()
    {
        // When adding types whose size exceeds 32-bits, add support in MessagePackSecurity.GetHashCollisionResistantEqualityComparer<T>()
        { typeof(Boolean), 0 },
        { typeof(Char), 1 },
        { typeof(SByte), 2 },
        { typeof(Byte), 3 },
        { typeof(Int16), 4 },
        { typeof(UInt16), 5 },
        { typeof(Int32), 6 },
        { typeof(UInt32), 7 },
        { typeof(Int64), 8 },
        { typeof(UInt64), 9 },
        { typeof(Single), 10 },
        { typeof(Double), 11 },
        // { typeof(DateTime), 12 },
        { typeof(string), 13 },
        // { typeof(byte[]), 14 },
    };

    public MRubyValue Serialize(object? value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value == null) return default;

        var type = value.GetType();

        switch (value)
        {
            case bool x:
                return MRubyValue.From(x);
            case short x:
                return MRubyValue.From(x);
            case ushort x:
                return MRubyValue.From(x);
            case int x:
                return MRubyValue.From(x);
            case uint x:
                return MRubyValue.From(x);
            case long x:
                return MRubyValue.From(x);
            case ulong x:
                return MRubyValue.From(x);
            case float x:
                return MRubyValue.From(x);
            case double x:
                return MRubyValue.From(x);
            case string x:
                return MRubyValue.From(state.NewString($"{x}"));
            case IDictionary d:
            {
                var hash = state.NewHash(d.Count);
                foreach (DictionaryEntry x in d)
                {
                    var k = Serialize(x.Key, state, options);
                    var v = Serialize(x.Value, state, options);
                    hash.Add(k, v);
                }
                return MRubyValue.From(hash);
            }
            case ICollection c:
            {
                var array = state.NewArray(c.Count);
                foreach (var x in c)
                {
                    array.Push(Serialize(x, state, options));
                }
                return MRubyValue.From(array);
            }
            default:
                if (type.IsEnum)
                {
                    return MRubyValue.From(state.NewString($"{value}"));
                }
                throw new MRubySerializationException($"Serialization not supported for type {value.GetType()}");
        }
    }

    public object? Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options)
    {
        if (value.IsNil)
        {
            return null;
        }

        switch (value.VType)
        {
            case MRubyVType.Nil:
                return null;
            case MRubyVType.False:
                return false;
            case MRubyVType.True:
                return true;
            case MRubyVType.Integer:
                return value.IntegerValue;
            case MRubyVType.Float:
                return value.FloatValue;
            case MRubyVType.Symbol:
            case MRubyVType.String:
                return value.As<RString>().ToString();
            case MRubyVType.Array:
            {
                var array = value.As<RArray>();
                var result = new object?[array.Length];
                for (var i = 0; i < array.Length; i++)
                {
                    var elementValue = state.Send(value, Names.OpAref, [MRubyValue.From(i)]);
                    var element = options.Resolver.GetFormatterWithVerify<object?>()
                        .Deserialize(elementValue, state, options);
                    result[i] = element;
                }
                return result;
            }
            case MRubyVType.Hash:
            {
                var dict = new Dictionary<object?, object?>();
                foreach (var x in value.As<RHash>())
                {
                    var k = options.Resolver.GetFormatterWithVerify<string>()
                        .Deserialize(x.Key, state, options);
                    var v = options.Resolver.GetFormatterWithVerify<object?>()
                        .Deserialize(x.Value, state, options);
                    dict.Add(k, v);
                }
                return dict;
            }
        }
        throw new MRubySerializationException($"Deserialization not supported `{value.VType}`");
    }

    // public static bool IsSupportedType(Type type, TypeInfo typeInfo, object? value)
    // {
    //     if (value == null)
    //     {
    //         return true;
    //     }
    //
    //     if (TypeToJumpCode.ContainsKey(type))
    //     {
    //         return true;
    //     }
    //
    //     if (typeInfo.IsEnum)
    //     {
    //         return true;
    //     }
    //
    //     if (value is System.Collections.IDictionary)
    //     {
    //         return true;
    //     }
    //
    //     if (value is System.Collections.ICollection)
    //     {
    //         return true;
    //     }
    //
    //     return false;
    // }
}