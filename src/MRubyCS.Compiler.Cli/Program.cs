using System.Buffers;
using System.Text;
using ConsoleAppFramework;
using MRubyCS;
using MRubyCS.Compiler;

var app = ConsoleApp.Create();
app.Add<Commands>();
app.Run(args);

enum OutputFormat
{
    Binary,
    CSharp,
}

class Commands
{
    /// <summary>
    /// Compile Ruby source file to mruby bytecode
    /// </summary>
    /// <param name="inputFile">Input Ruby source file path</param>
    /// <param name="outputFile">Output file path (default: stdout)</param>
    /// <param name="csharpNamespace">C# namespace for generated code</param>
    /// <param name="csharpClassName">C# class name for generated code</param>
    /// <param name="outputFormat">Output format: Binary or CSharp</param>
    [Command("compile")]
    public void Compile(
        [Argument] string inputFile,
        string? outputFile = null,
        string? csharpNamespace = null,
        string? csharpClassName = null,
        OutputFormat outputFormat = OutputFormat.Binary)
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
    }

    /// <summary>
    /// Dump mruby bytecode in human-readable format
    /// </summary>
    /// <param name="inputFile">Input file path (.rb source or .mrb bytecode)</param>
    /// <param name="outputFile">Output file path (default: stdout)</param>
    [Command("dump")]
    public void Dump(
        [Argument] string inputFile,
        string? outputFile = null)
    {
        var state = MRubyState.Create();
        var inputBytes = File.ReadAllBytes(inputFile);

        Irep irep;
        // Check if input is Ruby source or bytecode
        if (inputFile.EndsWith(".mrb", StringComparison.OrdinalIgnoreCase) ||
            (inputBytes.Length >= 4 && inputBytes[0] == 'R' && inputBytes[1] == 'I' && inputBytes[2] == 'T' && inputBytes[3] == 'E'))
        {
            // Already bytecode
            irep = state.ParseBytecode(inputBytes);
        }
        else
        {
            // Ruby source - compile first
            var compiler = MRubyCompiler.Create(state);
            using var bin = compiler.CompileToBytecode(inputBytes);
            irep = state.ParseBytecode(bin.AsSpan());
        }

        var bufferWriter = new ArrayBufferWriter<byte>();
        DumpIrepRecursive(state, irep, bufferWriter);

        using var outputStream = outputFile != null
            ? File.Create(outputFile)
            : Console.OpenStandardOutput();
        outputStream.Write(bufferWriter.WrittenSpan);
    }

    static void DumpIrepRecursive(MRubyState state, Irep irep, ArrayBufferWriter<byte> writer)
    {
        state.CodeDump(irep, writer);
        foreach (var child in irep.Children)
        {
            DumpIrepRecursive(state, child, writer);
        }
    }
}
