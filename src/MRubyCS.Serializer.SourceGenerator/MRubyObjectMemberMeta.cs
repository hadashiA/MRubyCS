using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MRubyCS.SourceGenerator;

namespace MRubyCS.Serializer.SourceGenerator;

class MRubyObjectMemberMeta
{
    public ISymbol Symbol { get; }
    public string Name { get; }
    public string FullTypeName { get; }
    public ITypeSymbol MemberType { get; }
    public bool IsField { get; }
    public bool IsProperty { get; }
    public bool IsSettable { get; }
    public bool HasKeyNameAlias { get; }
    public string KeyName { get; }

    public bool IsConstructorParameter { get; set; }
    public bool HasExplicitDefaultValueFromConstructor { get; set; }
    public object? ExplicitDefaultValueFromConstructor { get; set; }

    public byte[] KeyNameUtf8Bytes => keyNameUtf8Bytes ??= System.Text.Encoding.UTF8.GetBytes(KeyName);
    byte[]? keyNameUtf8Bytes;

    public MRubyObjectMemberMeta(ISymbol symbol, ReferenceSymbols references)
    {
        Symbol = symbol;
        Name = symbol.Name;
        KeyName = NamingConventionMutator.Mutate(Name, NamingConvention.SnakeCase);

        var memberAttribute = symbol.GetAttribute(references.MRubyMemberAttribute);
        if (memberAttribute != null)
        {
            if (memberAttribute.ConstructorArguments.Length > 0 &&
                memberAttribute.ConstructorArguments[0].Value is string aliasValue)
            {
                HasKeyNameAlias = true;
                KeyName = aliasValue;
            }
        }

        if (symbol is IFieldSymbol f)
        {
            IsProperty = false;
            IsField = true;
            IsSettable = !f.IsReadOnly; // readonly field can not set.
            MemberType = f.Type;

        }
        else if (symbol is IPropertySymbol p)
        {
            IsProperty = true;
            IsField = false;
            IsSettable = !p.IsReadOnly;
            MemberType = p.Type;
        }
        else
        {
            throw new Exception("member is not field or property.");
        }
        FullTypeName = MemberType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    public Location GetLocation(TypeDeclarationSyntax fallback)
    {
        var location = Symbol.Locations.FirstOrDefault() ?? fallback.Identifier.GetLocation();
        return location;
    }

    public string EmitDefaultValue()
    {
        if (!HasExplicitDefaultValueFromConstructor)
        {
            return (MemberType is { IsReferenceType: true, NullableAnnotation: NullableAnnotation.NotAnnotated })
                ? $"default({FullTypeName})!"
                : $"default({FullTypeName})";
        }

        return ExplicitDefaultValueFromConstructor switch
        {
            null => $"default({FullTypeName})",
            string x => $"\"{x}\"",
            float x => $"{x}f",
            double x => $"{x}d",
            decimal x => $"{x}m",
            bool x => x ? "true" : "false",
            _ => ExplicitDefaultValueFromConstructor.ToString()
        };
    }
}
