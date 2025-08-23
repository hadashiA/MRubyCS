namespace MRubyCS.Serializer;

public class MRubyValueSerializerOptions
{
    public static MRubyValueSerializerOptions Default => new()
    {
        Resolver = StandardResolver.Instance
    };

    public IMRubyValueFormatterResolver Resolver { get; set; } = StandardResolver.Instance;
}
