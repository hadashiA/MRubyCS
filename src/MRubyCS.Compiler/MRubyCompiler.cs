using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MRubyCS.Compiler
{
public class MRubyCompileException : Exception
{
    public MRubyCompileException(string message) : base(message)
    {
    }
}

public record MRubyCompileOptions
{
    public static MRubyCompileOptions Default { get; set; } = new();
}

public class MRubyCompiler : IDisposable
{
    public static MRubyCompiler Create(MRubyState mrb, MRubyCompileOptions? options = null)
    {
        var compilerStateHandle = MrbStateHandle.Create();
        return new MRubyCompiler(mrb, compilerStateHandle, options);
    }

    readonly MRubyState mruby;
    readonly MrbStateHandle compileStateHandle;
    readonly MRubyCompileOptions options;
    bool disposed;

    MRubyCompiler(
        MRubyState mruby,
        MrbStateHandle compileStateHandle,
        MRubyCompileOptions? options = null)
    {
        this.mruby = mruby;
        this.compileStateHandle = compileStateHandle;
        this.options = options ?? MRubyCompileOptions.Default;
    }

    ~MRubyCompiler()
    {
        Dispose(false);
    }

    public MRubyValue LoadSourceCodeFile(string path)
    {
        using var compilation = CompileFile(path);
        return mruby.LoadBytecode(compilation.AsBytecode());
    }

    public async Task<MRubyValue> LoadSourceCodeFileAsync(string path, CancellationToken cancellationToken = default)
    {
        using var compilation = await CompileFileAsync(path, cancellationToken);
        return mruby.LoadBytecode(compilation.AsBytecode());
    }

    public MRubyValue LoadSourceCode(ReadOnlySpan<byte> utf8Source)
    {
        using var compilation = Compile(utf8Source);
        return mruby.LoadBytecode(compilation.AsBytecode());
    }

    public MRubyValue LoadSourceCode(string source)
    {
        var utf8Source = Encoding.UTF8.GetBytes(source);
        return LoadSourceCode(utf8Source);
    }

    public RFiber LoadSourceCodeAsFiber(ReadOnlySpan<byte> utf8Source)
    {
        using var compilation = Compile(utf8Source);
        var proc = mruby.CreateProc(compilation.ToIrep(mruby));
        return mruby.CreateFiber(proc);
    }

    public RFiber LoadSourceCodeAsFiber(string source)
    {
        var utf8Source = Encoding.UTF8.GetBytes(source);
        return LoadSourceCodeAsFiber(utf8Source);
    }

    public CompilationResult CompileFile(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        return Compile(bytes);
    }

    public async Task<CompilationResult> CompileFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        return Compile(bytes);
    }

    public CompilationResult Compile(string sourceCode) =>
        Compile(Encoding.UTF8.GetBytes(sourceCode));

    public unsafe CompilationResult Compile(ReadOnlySpan<byte> utf8Source)
    {
        if (BomHelper.TryDetectEncoding(utf8Source, out var encoding))
        {
            if (encoding.Equals(Encoding.UTF8))
            {
                utf8Source = utf8Source[encoding.Preamble.Length..];
            }
            else
            {
                throw new MRubyCompileException("Only UTF-8 is supported");
            }
        }

        var context = MrcCContextHandle.Create(compileStateHandle);
        byte* bin = null;
        nint binLength = 0;
        fixed (byte* sourcePtr = utf8Source)
        {
            var irepPtr = NativeMethods.MrcLoadStringCxt(context.DangerousGetPtr(), &sourcePtr, utf8Source.Length);
            if (irepPtr == null || context.HasError)
            {
                // error
                return new CompilationResult(compileStateHandle, context);
            }
            NativeMethods.MrcDumpIrep(context.DangerousGetPtr(), irepPtr, 0, &bin, &binLength);
            return new CompilationResult(compileStateHandle, context, (IntPtr)bin, (int)binLength);
        }
    }

    public void Dispose(bool disposing)
    {
        if (disposed) return;
        disposed = true;
        compileStateHandle.Dispose();
        disposed = true;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
}
