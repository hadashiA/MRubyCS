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
    /// <param name="output">-o, Output file path (default: same directory as input with .mrb/.cs extension)</param>
    /// <param name="dump">Dump bytecode in human-readable format instead of compiling (outputs to stdout)</param>
    /// <param name="hir">Dump HIR (analysis IR with SSA + types) instead of bytecode (outputs to stdout)</param>
    /// <param name="hirNoTypes">Skip type inference when dumping HIR (faster, less informative)</param>
    /// <param name="hirShowEffects">Include per-insn effect classification in the HIR dump</param>
    /// <param name="hirShowDeadParams">Show all RegisterCount params per block (verbose)</param>
    /// <param name="hirOptimize">Run cleanup passes (MoveElim/ConstantFold/Dce) before dumping</param>
    /// <param name="format">Output format: binary or csharp</param>
    /// <param name="csharpNamespace">C# namespace for generated code</param>
    /// <param name="csharpClassName">C# class name for generated code</param>
    [Command("")]
    public void Run(
        [Argument] string inputFile,
        string? output = null,
        bool dump = false,
        bool hir = false,
        bool hirNoTypes = false,
        bool hirShowEffects = false,
        bool hirShowDeadParams = false,
        bool hirOptimize = false,
        OutputFormat format = OutputFormat.binary,
        string? csharpNamespace = null,
        string? csharpClassName = null)
    {
        var state = MRubyState.Create();
        var inputBytes = File.ReadAllBytes(inputFile);

        if (hir)
        {
            var irep = LoadIrep(state, inputFile, inputBytes);
            var opts = new MRubyCS.Hir.HirDumpOptions
            {
                State = state,
                ShowTypes = !hirNoTypes,
                ShowEffects = hirShowEffects,
                ShowDeadParams = hirShowDeadParams,
                RunOptimizations = hirOptimize,
            };
            var dumpText = state.DumpHir(irep, opts, runTypeInference: !hirNoTypes);

            using var outputStream = output is null or "-"
                ? Console.OpenStandardOutput()
                : File.Create(output);
            outputStream.Write(Encoding.UTF8.GetBytes(dumpText));
            return;
        }

        if (dump)
        {
            var irep = LoadIrep(state, inputFile, inputBytes);

            var bufferWriter = new ArrayBufferWriter<byte>();
            DumpIrepRecursive(state, irep, bufferWriter);

            using var outputStream = output is null or "-"
                ? Console.OpenStandardOutput()
                : File.Create(output);
            outputStream.Write(bufferWriter.WrittenSpan);
        }
        else
        {
            var compiler = MRubyCompiler.Create(state);
            using var compilation = compiler.Compile(inputBytes);

            using var outputStream = output == "-"
                ? Console.OpenStandardOutput()
                : File.Create(output ?? GetDefaultOutputPath(inputFile, format));

            switch (format)
            {
                case OutputFormat.binary:
                    outputStream.Write(compilation.AsBytecode());
                    break;
                case OutputFormat.csharp:
                    WriteCSharpOutput(outputStream, compilation.AsBytecode(), csharpNamespace, csharpClassName);
                    break;
            }
        }
    }

    static string GetDefaultOutputPath(string inputFile, OutputFormat format)
    {
        var extension = format switch
        {
            OutputFormat.csharp => ".cs",
            _ => ".mrb"
        };
        return Path.ChangeExtension(inputFile, extension);
    }

    static bool IsBytecode(string filePath, byte[] bytes)
    {
        return filePath.EndsWith(".mrb", StringComparison.OrdinalIgnoreCase) ||
               (bytes.Length >= 4 && bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'T' && bytes[3] == 'E');
    }

    static Irep LoadIrep(MRubyState state, string inputFile, byte[] inputBytes)
    {
        if (IsBytecode(inputFile, inputBytes))
        {
            return state.ParseBytecode(inputBytes);
        }
        var compiler = MRubyCompiler.Create(state);
        using var compilation = compiler.Compile(inputBytes);
        return state.ParseBytecode(compilation.AsBytecode());
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
