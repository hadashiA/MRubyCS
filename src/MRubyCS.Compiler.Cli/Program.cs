using System.Text;
using ConsoleAppFramework;
using MRubyCS;
using MRubyCS.Compiler;

// TODO: production ready

ConsoleApp.Run(args, (
    [Argument]
    string inputFile,
    string? outputFile = null,
    string? csharpNamespace = null,
    string? csharpClassName = null,
    OutputFormat outputFormat = OutputFormat.Binary) =>
{
    var state = MRubyState.Create();
    var compiler = MRubyCompiler.Create(state);

    var inputBytes = File.ReadAllBytes(inputFile);
    using var bin = compiler.CompileToBytecode(inputBytes);

    using var outputStream = outputFile != null
        ? File.Create(outputFile)
        : Console.OpenStandardOutput();

    switch (outputFormat)
    {
        case OutputFormat.Binary:
            outputStream.Write(bin.AsSpan());
            break;
        case OutputFormat.CSharp:
            var stringBuilder = new StringBuilder();
            if (csharpNamespace != null)
            {
                stringBuilder.AppendLine($"namespace {csharpNamespace} {{");
            }
            stringBuilder.AppendLine($$"""
public static class {{csharpClassName ?? "MRubyBytecodeEmbedded"}}
{
    public static readonly byte[] Bytes =
    [
""");
            var i = 0;
            const string indent = "        ";
            stringBuilder.Append(indent);
            foreach (var b in bin.AsSpan())
            {
                stringBuilder.Append($"0x{b:X2}, ");
                if (++i >= 16)
                {
                    stringBuilder.AppendLine();
                    stringBuilder.Append(indent);
                    i = 0;
                }
            }
            stringBuilder.AppendLine($$"""

    ];
}
""");
            if (csharpNamespace != null)
            {
                stringBuilder.AppendLine("}");
            }
            using (var writer = new StreamWriter(outputStream))
            {
                writer.Write(stringBuilder.ToString());
                writer.Flush();
            }
            break;
    }
});

enum OutputFormat
{
    Binary,
    CSharp,
}
