using Microsoft.CodeAnalysis;

namespace MRubyCS.Serializer.SourceGenerator;

public class ReferenceSymbols
{
    public static ReferenceSymbols? Create(Compilation compilation)
    {
        var mrubyObjectAttribute = compilation.GetTypeByMetadataName("MRubyCS.Serializer.MRubyObjectAttribute");
        if (mrubyObjectAttribute is null)
            return null;

        return new ReferenceSymbols
        {
            MRubyObjectAttribute = mrubyObjectAttribute,
            MRubyMemberAttribute = compilation.GetTypeByMetadataName("MRubyCS.Serializer.MRubyMemberAttribute")!,
            MRubyIgnoreAttribute = compilation.GetTypeByMetadataName("MRubyCS.Serializer.MRubyIgnoreAttribute")!,
            MRubyConstructorAttribute = compilation.GetTypeByMetadataName("MRubyCS.Serializer.MRubyConstructorAttribute")!,
        };
    }

    public INamedTypeSymbol MRubyObjectAttribute { get; private set; } = default!;
    public INamedTypeSymbol MRubyMemberAttribute { get; private set; } = default!;
    public INamedTypeSymbol MRubyIgnoreAttribute { get; private set; } = default!;
    public INamedTypeSymbol MRubyConstructorAttribute { get; private set; } = default!;
}
