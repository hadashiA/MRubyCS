using System;
using System.IO;
using System.Runtime.InteropServices;
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

    MRubyCompiler(
        MRubyState mruby,
        MrbStateHandle compileStateHandle,
        MRubyCompileOptions? options = null)
    {
        this.mruby = mruby;
        this.compileStateHandle = compileStateHandle;
        this.options = options ?? MRubyCompileOptions.Default;
    }

    public MRubyValue LoadSourceCodeFile(string path)
    {
        return mruby.Execute(CompileFile(path));
    }

    public async Task<MRubyValue> LoadSourceCodeFileAsync(string path, CancellationToken cancellationToken = default)
    {
        return mruby.Execute(await CompileFileAsync(path, cancellationToken));
    }

    public MRubyValue LoadSourceCode(ReadOnlySpan<byte> utf8Source)
    {
        return mruby.Execute(Compile(utf8Source));
    }

    public MRubyValue LoadSourceCode(string source)
    {
        var utf8Source = Encoding.UTF8.GetBytes(source);
        return LoadSourceCode(utf8Source);
    }

    public RFiber LoadSourceCodeAsFiber(ReadOnlySpan<byte> utf8Source)
    {
        var irep = Compile(utf8Source);
        var proc = mruby.CreateProc(irep);
        return mruby.CreateFiber(proc);
    }

    public RFiber LoadSourceCodeAsFiber(string source)
    {
        var utf8Source = Encoding.UTF8.GetBytes(source);
        return LoadSourceCodeAsFiber(utf8Source);
    }

    [Obsolete("Use CompileToBytecode instead")]
    public MrbNativeBytesHandle CompileToBinaryFormat(ReadOnlySpan<byte> utf8Source) =>
        CompileToBytecode(utf8Source);

    public unsafe MrbNativeBytesHandle CompileToBytecode(string source) =>
        CompileToBytecode(Encoding.UTF8.GetBytes(source));

    public unsafe MrbNativeBytesHandle CompileToBytecode(ReadOnlySpan<byte> utf8Source)
    {
        var mrbPtr = compileStateHandle.DangerousGetPtr();
        byte* bin = null;
        var binLength = 0;
        byte* errorMessageCStr = null;
        int resultCode;
        fixed (byte* sourcePtr = utf8Source)
        {
            resultCode = NativeMethods.MrbcsCompile(
                mrbPtr,
                sourcePtr,
                utf8Source.Length,
                &bin,
                &binLength,
                &errorMessageCStr);
        }

        if (resultCode != NativeMethods.Ok)
        {
            if (errorMessageCStr != null)
            {
                var errorMessage = Marshal.PtrToStringUTF8((IntPtr)errorMessageCStr)!;
                throw new MRubyCompileException(errorMessage);
            }
        }
        return new MrbNativeBytesHandle(compileStateHandle, (IntPtr)bin, binLength);
    }

    public Irep CompileFile(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        return Compile(bytes);
    }

    public async Task<Irep> CompileFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        return Compile(bytes);
    }

    public unsafe Irep Compile(ReadOnlySpan<byte> utf8Source)
    {
        var mrbPtr = compileStateHandle.DangerousGetPtr();
        byte* bin = null;
        var binLength = 0;
        byte* errorMessageCStr = null;
        int resultCode;
        fixed (byte* codePtr = utf8Source)
        {
            resultCode = NativeMethods.MrbcsCompile(
                mrbPtr,
                codePtr,
                utf8Source.Length,
                &bin,
                &binLength,
                &errorMessageCStr);
        }

        try
        {
            if (resultCode != NativeMethods.Ok)
            {
                if (errorMessageCStr != null)
                {
                    var errorMessage = Marshal.PtrToStringUTF8((IntPtr)errorMessageCStr)!;
                    throw new MRubyCompileException(errorMessage);
                }
            }
            var span = new ReadOnlySpan<byte>(bin, binLength);
            return mruby.RiteParser.Parse(span);
        }
        finally
        {
            if (bin != null)
            {
                NativeMethods.MrbFree(mrbPtr, bin);
            }
        }
    }

    public void Dispose()
    {
        compileStateHandle.Dispose();
        GC.SuppressFinalize(this);
    }
}
}
