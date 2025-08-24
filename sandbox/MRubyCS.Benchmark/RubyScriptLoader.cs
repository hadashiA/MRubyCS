using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MRubyCS.Compiler;

namespace MRubyCS.Benchmark;

unsafe class RubyScriptLoader : IDisposable
{
    readonly MRubyState mrubyCSState;
    readonly MrbStateNative* mrbStateNative;

    readonly MRubyCompiler mrubyCSCompiler;
    bool disposed;

    Irep? currentMRubyCSIrep;
    RProcHandle? currentMRubyNativeProc;

    public RubyScriptLoader()
    {
        mrubyCSState = MRubyState.Create();
        mrubyCSCompiler = MRubyCompiler.Create(mrubyCSState);

        mrbStateNative = NativeMethods.MrbOpen();
    }

    public void IncludeMathModule()
    {
        var mathModule = mrubyCSState.DefineModule(mrubyCSState.Intern("Math"u8), mrubyCSState.ObjectClass);
        mrubyCSState.DefineMethod(mathModule, mrubyCSState.Intern("sqrt"u8), MathMembers.Sqrt);
        mrubyCSState.DefineMethod(mathModule, mrubyCSState.Intern("sin"u8), MathMembers.Sin);
        mrubyCSState.DefineMethod(mathModule, mrubyCSState.Intern("cos"u8), MathMembers.Cos);
        mrubyCSState.IncludeModule(mrubyCSState.ObjectClass, mathModule);
    }

    public void PreloadScript(ReadOnlySpan<byte> source)
    {
        currentMRubyCSIrep = mrubyCSCompiler.Compile(source);

        currentMRubyNativeProc?.Dispose();

        RProcNative* procPtr = null;
        byte* errorMessageCStr = null;
        fixed (byte* sourcePtr = source)
        {
            var resultCode = NativeMethods.MrbcsCompileToProc(
                mrbStateNative,
                sourcePtr,
                source.Length,
                &procPtr,
                &errorMessageCStr);

            if (resultCode != 0)
            {
                if (errorMessageCStr != null)
                {
                    var errorMessage = Marshal.PtrToStringUTF8((IntPtr)errorMessageCStr)!;
                    throw new MRubyCompileException(errorMessage);
                }
            }
        }

        currentMRubyNativeProc = new RProcHandle(mrbStateNative, procPtr);
    }

    public void PreloadScriptFromFile(string fileName)
    {
        var source = ReadBytes(fileName);
        PreloadScript(source);
    }

    public MRubyValue RunMRubyCS()
    {
        return mrubyCSState.Execute(currentMRubyCSIrep!);
    }

    public MrbValueNative RunMRubyNative()
    {
        return NativeMethods.MrbLoadProc(mrbStateNative, currentMRubyNativeProc!.DangerousGetPtr());
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposed)
        {
            return;
        }

        currentMRubyNativeProc?.Dispose();
        NativeMethods.MrbClose(mrbStateNative);
        disposed = true;
    }

    static string GetAbsolutePath(string relativePath, [CallerFilePath] string callerFilePath = "")
    {
        return Path.Join(Path.GetDirectoryName(callerFilePath)!, relativePath);
        // return Path.Join(Assembly.GetEntryAssembly()!.Location, relativePath);
    }

    byte[] ReadBytes(string fileName)
    {
        var path = GetAbsolutePath(Path.Join("ruby", fileName));
        return File.ReadAllBytes(path);
    }
}