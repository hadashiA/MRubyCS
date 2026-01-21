using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace MRubyCS.Compiler
{
public class CompilationResult : IDisposable
{
    public IReadOnlyList<DiagnosticsDescriptor> Diagnostics => diagnostics;
    public bool HasError => contextHandle.HasError;

    readonly MrbStateHandle stateHandle;
    readonly MrcCContextHandle contextHandle;
    readonly IntPtr bytecodeDataPtr;
    readonly int bytecodeLength;
    readonly IReadOnlyList<DiagnosticsDescriptor> diagnostics;
    bool disposed;

    internal CompilationResult(
        MrbStateHandle stateHandle,
        MrcCContextHandle contextHandle,
        IntPtr bytecodeDataPtr,
        int bytecodeLength)
    {
        this.stateHandle = stateHandle;
        this.contextHandle = contextHandle;
        this.bytecodeDataPtr = bytecodeDataPtr;
        this.bytecodeLength = bytecodeLength;
        diagnostics = contextHandle.GetDiagnostics();
    }

    internal CompilationResult(
        MrbStateHandle stateHandle,
        MrcCContextHandle contextHandle)
    {
        this.stateHandle = stateHandle;
        this.contextHandle = contextHandle;
        diagnostics = contextHandle.GetDiagnostics();
    }

    ~CompilationResult()
    {
        Dispose(disposing: false);
    }

    public unsafe ReadOnlySpan<byte> AsBytecode()
    {
        return new ReadOnlySpan<byte>((byte*)bytecodeDataPtr, bytecodeLength);
    }

    public ReadOnlySpan<byte> AsSpan() => AsBytecode();

    public Irep ToIrep(MRubyState mrubycsState)
    {
        return mrubycsState.RiteParser.Parse(AsBytecode());
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this); // ファイナライザの呼び出しを抑制
    }

    unsafe void Dispose(bool disposing)
    {
        if (disposed) return;

        if (bytecodeDataPtr != IntPtr.Zero)
        {
            // NativeMethods.MrcFree(stateHandle.DangerousGetPtr(), bytecodeDataPtr.ToPointer());
            NativeMethods.MrcFree(bytecodeDataPtr.ToPointer());
        }

        contextHandle.Dispose();
        disposed = true;
    }
}
}
