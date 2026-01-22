using System.Buffers;
using System.Text;
using ConsoleAppFramework;
using MRubyCS;
using MRubyCS.Compiler;

var app = ConsoleApp.Create();
app.Add<Commands>();
app.Run(args);

class Commands
{
    /// <summary>
    /// Compile Ruby source file to mruby bytecode
    /// </summary>
    /// <param name="inputFile">Input Ruby source file path</param>
    /// <param name="output">-o, Output file path (default: stdout)</param>
    /// <param name="dump">Dump bytecode in human-readable format instead of compiling</param>
    /// <param name="format">Output format: binary or csharp</param>
    /// <param name="csharpNamespace">C# namespace for generated code</param>
    /// <param name="csharpClassName">C# class name for generated code</param>
    [Command("")]
    public void Run(
        [Argument] string inputFile,
        string? output = null,
        bool dump = false,
        OutputFormat format = OutputFormat.binary,
        string? csharpNamespace = null,
        string? csharpClassName = null)
    {
        var state = MRubyState.Create();
        var inputBytes = File.ReadAllBytes(inputFile);

        if (dump)
        {
            Irep irep;
            if (IsBytecode(inputFile, inputBytes))
            {
                irep = state.ParseBytecode(inputBytes);
            }
            else
            {
                var compiler = MRubyCompiler.Create(state);
                using var bin = compiler.CompileToBytecode(inputBytes);
                irep = state.ParseBytecode(bin.AsSpan());
            }

            var bufferWriter = new ArrayBufferWriter<byte>();
            DumpIrepRecursive(state, irep, bufferWriter);

            using var outputStream = output != null
                ? File.Create(output)
                : Console.OpenStandardOutput();
            outputStream.Write(bufferWriter.WrittenSpan);
        }
        else
        {
            var compiler = MRubyCompiler.Create(state);
            using var bin = compiler.CompileToBytecode(inputBytes);

            using var outputStream = output != null
                ? File.Create(output)
                : Console.OpenStandardOutput();

            switch (format)
            {
                case OutputFormat.binary:
                    outputStream.Write(bin.AsSpan());
                    break;
                case OutputFormat.csharp:
                    WriteCSharpOutput(outputStream, bin.AsSpan(), csharpNamespace, csharpClassName);
                    break;
            }
        }
    }

    static bool IsBytecode(string filePath, byte[] bytes)
    {
        return filePath.EndsWith(".mrb", StringComparison.OrdinalIgnoreCase) ||
               (bytes.Length >= 4 && bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'T' && bytes[3] == 'E');
    }

    static void DumpIrepRecursive(MRubyState state, Irep irep, ArrayBufferWriter<byte> writer)
    {
        state.CodeDump(irep, writer);
        foreach (var child in irep.Children)
        {
            DumpIrepRecursive(state, child, writer);
        }
    }

    static void WriteCSharpOutput(Stream outputStream, ReadOnlySpan<byte> bytecode, string? ns, string? className)
    {
        var sb = new StringBuilder();
        if (ns != null)
        {
            sb.AppendLine($"namespace {ns} {{");
        }
        sb.AppendLine($$"""
public static class {{className ?? "MRubyBytecodeEmbedded"}}
{
    public static readonly byte[] Bytes =
    [
""");
        var i = 0;
        const string indent = "        ";
        sb.Append(indent);
        foreach (var b in bytecode)
        {
            sb.Append($"0x{b:X2}, ");
            if (++i >= 16)
            {
                sb.AppendLine();
                sb.Append(indent);
                i = 0;
            }
        }
        sb.AppendLine("""

    ];
}
""");
        if (ns != null)
        {
            sb.AppendLine("}");
        }
        using var writer = new StreamWriter(outputStream);
        writer.Write(sb.ToString());
        writer.Flush();
    }
}

enum OutputFormat
{
    binary,
    csharp,
}
