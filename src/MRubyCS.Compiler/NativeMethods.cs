using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace MRubyCS.Compiler
{
[StructLayout(LayoutKind.Explicit)]
struct MrbState { }

[StructLayout(LayoutKind.Explicit)]
struct MrcIrep { }

[StructLayout(LayoutKind.Sequential)]
unsafe struct MrcCContext
{
    public MrbState* Mrb;
    public void* Jmp;
    public void* P;
    public void* Options;
    public int SLen;
    public byte* Filename;
    public ushort LineNo;
    public void* TargetClass;
    public byte CaptureErrors;
    public byte DumpResult;
    public byte NoExec;
    public byte KeepLv;
    public byte NoOptimize;
    public byte NoExtOps;
    public void* Upper;
    public MrcDiagnosticList* DiagnosticList;
}

enum MrcDiagnosticCode
{
    Warning = 0,
    Error = 1,
    GeneratorWarning = 2,
    GeneratorError = 3,
}

[StructLayout(LayoutKind.Sequential)]
unsafe struct MrcDiagnosticList
{
    public MrcDiagnosticCode DiagnosticCode;
    public byte* Message;
    public int Line;
    public int Column;
    public MrcDiagnosticList* Next;
}

unsafe class NativeMethods
{
    const string DllName = "libmruby";

    public const int Ok = 0;
    public const int Failed = 11;

#if !UNITY_2021_3_OR_NEWER
    static NativeMethods()
    {
        NativeLibrary.SetDllImportResolver(typeof(NativeMethods).Assembly, DllImportResolver);
    }

    static IntPtr DllImportResolver(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName == DllName)
        {
            var path = "runtimes";
            string extname;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                path = Path.Join(path, "win");
                extname = ".dll";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                path = Path.Join(path, "osx");
                extname = ".dylib";
            }
            else
            {
                path = Path.Join(path, "linux");
                extname = ".so";
            }

            switch (RuntimeInformation.ProcessArchitecture)
            {
                case Architecture.X86:
                    path += "-x86";
                    break;
                case Architecture.X64:
                    path += "-x64";
                    break;
                case Architecture.Arm64:
                    path += "-arm64";
                    break;
            }

            path = Path.Join(path, "native", $"{DllName}{extname}");
            return NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, path), assembly, searchPath);
        }
        return IntPtr.Zero;
    }
#endif
    [DllImport(DllName, EntryPoint = "mrb_open", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern MrbState* MrbOpen();

    [DllImport(DllName, EntryPoint = "mrb_close", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void MrbClose(MrbState* mrb);

    [DllImport(DllName, EntryPoint = "mrubycs_free", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void MrcFree(void* ptr);

    [DllImport(DllName, EntryPoint = "mrc_ccontext_new", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern MrcCContext* MrcCContextNew(MrbState* mrb);

    [DllImport(DllName, EntryPoint = "mrc_ccontext_free", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern MrcCContext* MrcCContextFree(MrcCContext* c);

    [DllImport(DllName, EntryPoint = "mrc_load_string_cxt", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern MrcIrep* MrcLoadStringCxt(
        MrcCContext* c,
        byte** source,
        nint sourceLength);

    // int mrc_dump_irep(mrc_ccontext *c, const mrc_irep *irep, uint8_t flags, uint8_t **bin, size_t *bin_size);
    [DllImport(DllName, EntryPoint = "mrc_dump_irep", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void MrcDumpIrep(
        MrcCContext* c,
        MrcIrep* irep,
        byte flags,
        byte** bin,
        nint* binSize);

    [DllImport(DllName, EntryPoint = "mrc_irep_free", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void MrcIrepFree(MrcCContext* c, MrcIrep* irep);

    [DllImport(DllName, EntryPoint = "mrc_diagnostic_list_free", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void MrcDiagnosticListFree(MrcCContext* c);

}
}
