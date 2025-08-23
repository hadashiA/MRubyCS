namespace MRubyCS.Serializer;

public interface IMRubyValueFormatter;

public interface IMRubyValueFormatter<T> : IMRubyValueFormatter
{
    MRubyValue Serialize(T value, MRubyState state, MRubyValueSerializerOptions options);
    T Deserialize(MRubyValue value, MRubyState state, MRubyValueSerializerOptions options);
}
