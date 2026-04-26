using System.Text;

namespace MRubyCS.SourceGenerator;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

sealed class ConstSymbolDefinition
{
    const uint OffsetBasis = 2166136261u;
    const uint FnvPrime = 16777619u;

    const string PackTable = "_abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    const int PackLengthMax = 5;

    public uint Index { get; }
    public string SymbolName { get; }
    public string VariableName { get; }
    public byte[] Utf8 { get; }
    public int HashCode { get; }
    public bool IsPackable { get; }

    public ConstSymbolDefinition(uint index, string symbolName, string variableName, bool isPackable)
    {
        Index = index;
        SymbolName = symbolName;
        VariableName = variableName;
        IsPackable = isPackable;
        Utf8 = Encoding.UTF8.GetBytes(symbolName);

        var hash = OffsetBasis;
        foreach (var b in symbolName)
        {
            hash ^= b;
            hash *= FnvPrime;
        }
        HashCode = unchecked((int)hash);
    }

    public static uint? TryPack(string name)
    {
        if (name.Length == 0 || name.Length > PackLengthMax) return null;
        uint packed = 0;
        for (var i = 0; i < name.Length; i++)
        {
            var x = PackTable.IndexOf(name[i]);
            if (x < 0) return null;
            var bits = (uint)x + 1;
            packed |= bits << (24 - i * 6);
        }
        return packed;
    }
}

[Generator(LanguageNames.CSharp)]
public class MRubyCSSourceGenerator : IIncrementalGenerator
{
    static readonly KeyValuePair<string, string>[] KnownSymbols =
    [
        new("ClassNameKey", "__classname__"),
        new("OuterKey", "__outer__"),
        new("OuterClassKey", "__outerclass__"),
        new("AttachedKey", "__attached__"),
        new("InheritedKey", "__inherited__"),
        new("IdKey", "__id__"),

        new("NameVariable", "@name"),
        new("ArgsVariable", "@args"),

        new("New", "new"),
        new("Id", "id"),
        new("Send", "send"),
        new("Dup", "dup"),
        new("Clone", "clone"),
        new("ToS", "to_s"),
        new("ToSym", "to_sym"),
        new("ToA", "to_a"),
        new("ToI", "to_i"),
        new("ToF", "to_f"),
        new("Inspect", "inspect"),
        new("Raise", "raise"),
        new("Name", "name"),
        new("Class", "class"),
        new("Default", "default"),
        new("Initialize", "initialize"),
        new("InitializeCopy", "initialize_copy"),
        new("InstanceEval", "instance_eval"),
        new("MethodAdded", "method_added"),
        new("SingletonMethodAdded", "singleton_method_added"),
        new("Nil", "nil"),
        new("SuperClass", "superclass"),
        new("Inherited", "inherited"),
        new("ExtendObject", "extend_object"),
        new("Extended", "extended"),
        new("Prepended", "prepended"),
        new("Private", "private"),
        new("Protected", "protected"),
        new("ClassEval", "class_eval"),
        new("ModuleEval", "module_eval"),
        new("Hash", "hash"),
        new("Exception", "exception"),
        new("Call", "call"),
        new("MethodMissing", "method_missing"),

        new("QNil", "nil?"),
        new("QEqual", "equal?"),
        new("QEql", "eql?"),
        new("QBlockGiven", "block_given?"),
        new("QRespondTo", "respond_to?"),
        new("QRespondToMissing", "respond_to_missing?"),
        new("QInclude", "include?"),
        new("QIsA", "is_a?"),
        new("QInstanceOf", "instance_of?"),
        new("QKindOf", "kind_of?"),

        new("OpNot", "!"),
        new("OpMod", "%"),
        new("OpAnd", "&"),
        new("OpMul", "*"),
        new("OpAdd", "+"),
        new("OpSub", "-"),
        new("OpDiv", "/"),
        new("OpLt", "<"),
        new("OpLe", "<="),
        new("OpGt", ">"),
        new("OpGe", ">="),
        new("OpXor", "^"),
        new("OpTick", "`"),
        new("OpOr", "|"),
        new("OpNeg", "~"),
        new("OpNeq", "!="),
        new("OpMatch", "~="),
        new("OpAndAnd", "&&"),
        new("OpPow", "**"),
        new("OpPlus", "+@"),
        new("OpMinus", "-@"),
        new("OpLShift", "<<"),
        new("OpRShift", ">>"),
        new("OpEq", "=="),
        new("OpEqq", "==="),
        new("OpCmp", "<=>"),
        new("OpAref", "[]"),
        new("OpAset", "[]="),
        new("OpOrOr", "||"),

        new("BasicObjectClass", "BasicObject"),
        new("ObjectClass", "Object"),
        new("ModuleClass", "Module"),
        new("ClassClass", "Class"),
        new("ExceptionClass", "Exception"),
        new("RuntimeError", "RuntimeError"),
        new("TypeError", "TypeError"),
        new("ZeroDivisionError", "ZeroDivisionError"),
        new("ArgumentError", "ArgumentError"),
        new("NoMethodError", "NoMethodError"),
        new("NameError", "NameError"),
        new("IndexError", "IndexError"),
        new("RangeError", "RangeError"),
        new("FrozenError", "FrozenError"),
        new("NotImplementedError", "NotImplementedError"),
        new("LocalJumpError", "LocalJumpError"),
        new("SystemStackError", "SystemStackError"),
        new("FloatDomainError", "FloatDomainError"),
        new("KeyError", "KeyError"),
        new("FiberError", "FiberError"),
    ];

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static initContext =>
        {
            var stringBuilder = new StringBuilder();

            // Inline-packable presyms use their packed value as the ID; non-packable
            // presyms are assigned sequential IDs starting from 1. The two ranges never
            // collide because packed values are always >= 1<<24.
            uint nonPackableSeq = 0;
            var definitions = KnownSymbols
                .Select(kvp =>
                {
                    var packed = ConstSymbolDefinition.TryPack(kvp.Value);
                    var isPackable = packed.HasValue;
                    var index = isPackable ? packed!.Value : ++nonPackableSeq;
                    return new ConstSymbolDefinition(index, kvp.Value, kvp.Key, isPackable);
                })
                .ToArray();

            var nonPackableDefinitions = definitions.Where(x => !x.IsPackable)
                .OrderBy(x => x.Index)
                .ToArray();
            var packableDefinitions = definitions.Where(x => x.IsPackable).ToArray();

            // Hash-based dispatch covers non-packable presyms only; packable presyms are
            // resolved by Symbol.TryInlinePack in the caller.
            var nonPackableByHashCode = nonPackableDefinitions.ToLookup(
                x => x.HashCode,
                x => x);

            stringBuilder.AppendLine($$"""
// <auto-generated />
using System;
using System.Runtime.CompilerServices;

namespace MRubyCS;

static class Names
{
    /// <summary>Number of non-packable presyms. Dynamic symbols are issued starting from Count + 1.</summary>
    public static int Count => {{nonPackableDefinitions.Length}};

    static readonly byte[][] Utf8Names =
    [
""");
            foreach (var x in nonPackableDefinitions)
            {
                var byteArrayString = string.Join(", ", x.Utf8.Select(x => x.ToString()));
                stringBuilder.AppendLine($$"""
        [{{byteArrayString}}], // "{{x.SymbolName}}"
""");
            }
            stringBuilder.AppendLine($$"""
    ];

""");
            // Static byte arrays for packable presyms so NameOf can return them
            // without allocating.
            foreach (var x in packableDefinitions)
            {
                var byteArrayString = string.Join(", ", x.Utf8.Select(x => x.ToString()));
                stringBuilder.AppendLine($$"""
    static readonly byte[] _packed_{{x.VariableName}} = [{{byteArrayString}}]; // "{{x.SymbolName}}" packed=0x{{x.Index:X}}
""");
            }
            stringBuilder.AppendLine();

            foreach (var x in definitions)
            {
                var packedComment = x.IsPackable ? $" (inline-packed = 0x{x.Index:X})" : "";
                stringBuilder.AppendLine($$"""
    /// <summary>
    /// Known symbol ("{{x.SymbolName}}"){{packedComment}}
    /// </summary>
    public static Symbol {{x.VariableName}}
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new({{x.Index}}u);
    }

""");
            }

            stringBuilder.AppendLine($$"""
    public static bool TryFind(int hashCode, ReadOnlySpan<byte> name, out Symbol symbol)
    {
        switch (hashCode)
        {
""");
            foreach (var x in nonPackableByHashCode)
            {
                stringBuilder.AppendLine($$"""
            case {{x.Key}}:
""");
                if (x.Count() == 1)
                {
                    var singleValue = x.First();
                    stringBuilder.AppendLine($$"""
                if (name.SequenceEqual("{{singleValue.SymbolName}}"u8))
                {
                    symbol = new Symbol({{singleValue.Index}}u);
                    return true;
                }
                break;
""");
                }
                else
                {
                    var branch = "if";
                    foreach (var xs in x)
                    {
                        stringBuilder.AppendLine($$"""
                {{branch}} (name.SequenceEqual("{{xs.SymbolName}}"u8))
                {
                    symbol = new Symbol({{xs.Index}}u);
                    return true;
                }
""");
                        branch = "else if";
                    }
                    stringBuilder.AppendLine($$"""
                break;
""");
                }
            }
            stringBuilder.AppendLine($$"""
        }
        symbol = default;
        return false;
    }

    public static bool TryGetName(Symbol symbol, out ReadOnlySpan<byte> name)
    {
        var v = symbol.Value;
        if (v > 0 && v <= (uint)Count)
        {
            name = Utf8Names[(int)v - 1];
            return true;
        }
        // Skip the packable-presym switch for dynamic symbols (the dominant case
        // at runtime). All packable presyms have IDs >= 1<<24 by construction.
        if (v < (1u << 24))
        {
            name = default!;
            return false;
        }
        switch (v)
        {
""");
            foreach (var x in packableDefinitions)
            {
                stringBuilder.AppendLine($$"""
            case {{x.Index}}u: // "{{x.SymbolName}}"
                name = _packed_{{x.VariableName}};
                return true;
""");
            }
            stringBuilder.AppendLine($$"""
        }
        name = default!;
        return false;
    }
}
""");
            // Use UTF-8 without BOM so the generated file does not start with EF BB BF.
            var sourceText = SourceText.From(stringBuilder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            initContext.AddSource("KnownSymbols.g.cs", sourceText);
        });
    }
}
