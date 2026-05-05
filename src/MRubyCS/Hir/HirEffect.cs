using System;

namespace MRubyCS.Hir;

// Categories of side effects an HIR insn can produce. Used by DCE / hoisting /
// allocation-elision passes to decide what's safe to reorder, drop, or replace.
[Flags]
public enum HirEffectBits : ushort
{
    None        = 0,
    Allocate    = 1 << 0,    // Heap allocation (NewArray, NewHash, Lambda, NewString...)
    IvarRead    = 1 << 1,    // Reads an instance variable on some object
    IvarWrite   = 1 << 2,
    GvarRead    = 1 << 3,
    GvarWrite   = 1 << 4,
    CvarRead    = 1 << 5,
    CvarWrite   = 1 << 6,
    ConstRead   = 1 << 7,
    ConstWrite  = 1 << 8,
    UpVarRead   = 1 << 9,
    UpVarWrite  = 1 << 10,
    MethodTable = 1 << 11,    // Class definition / method add / remove
    Raise       = 1 << 12,    // May raise an exception
    Call        = 1 << 13,    // Performs a Ruby method dispatch (worst-case: any side effect)
    Return      = 1 << 14,    // Control flow leaves the function

    AnyWrite = IvarWrite | GvarWrite | CvarWrite | ConstWrite | UpVarWrite | MethodTable,
    AnyRead  = IvarRead | GvarRead | CvarRead | ConstRead | UpVarRead,
    AnySideEffect = AnyWrite | Raise | Call | Return | Allocate | MethodTable,
}

public readonly struct HirEffect(HirEffectBits read, HirEffectBits write)
{
    public static readonly HirEffect None = default;
    public static HirEffect Pure => default;
    public static HirEffect Allocates => new(HirEffectBits.None, HirEffectBits.Allocate);

    // Worst case: a Send may do anything. Used as a conservative default until
    // type-specialization narrows resolved methods.
    public static HirEffect AnyCall => new(HirEffectBits.AnyRead | HirEffectBits.AnySideEffect,
        HirEffectBits.AnySideEffect);

    // Pure value reads (no observable side effect, but reads global state).
    public static HirEffect ReadIvar => new(HirEffectBits.IvarRead, HirEffectBits.None);
    public static HirEffect ReadGvar => new(HirEffectBits.GvarRead, HirEffectBits.None);
    public static HirEffect ReadConst => new(HirEffectBits.ConstRead, HirEffectBits.None);

    public HirEffectBits Read => read;
    public HirEffectBits Write => write;

    public bool IsPure => Read == HirEffectBits.None && Write == HirEffectBits.None;
    public bool MayWrite(HirEffectBits cat) => (Write & cat) != HirEffectBits.None;
    public bool MayRead(HirEffectBits cat) => (Read & cat) != HirEffectBits.None;

    public override string ToString()
    {
        if (IsPure) return "pure";
        var r = Read == HirEffectBits.None ? "" : $"r={Read}";
        var w = Write == HirEffectBits.None ? "" : $"w={Write}";
        return $"{r}{(r.Length > 0 && w.Length > 0 ? "," : "")}{w}";
    }

    // Default effect classification for an Insn based purely on its kind.
    // Passes can override this when more is known (e.g. type spec narrows a
    // Send to a leaf method).
    public static HirEffect ForKind(HirInsnKind kind) => kind switch
    {
        // No-effect immediates
        HirInsnKind.Param or HirInsnKind.Nop or HirInsnKind.LoadNil
            or HirInsnKind.LoadTrue or HirInsnKind.LoadFalse or HirInsnKind.LoadSelf
            or HirInsnKind.LoadInt or HirInsnKind.LoadSym or HirInsnKind.LoadPool
            or HirInsnKind.Move
            => Pure,

        // Allocations
        HirInsnKind.NewArray or HirInsnKind.NewHash or HirInsnKind.NewString
            or HirInsnKind.NewRange or HirInsnKind.Lambda or HirInsnKind.Block
            or HirInsnKind.Method or HirInsnKind.LoadString
            => Allocates,

        // Reads
        HirInsnKind.GetIV => ReadIvar,
        HirInsnKind.GetGV or HirInsnKind.GetSV => ReadGvar,
        HirInsnKind.GetCV => new(HirEffectBits.CvarRead, HirEffectBits.None),
        HirInsnKind.GetConst or HirInsnKind.GetMCnst => ReadConst,
        HirInsnKind.GetUpVar => new(HirEffectBits.UpVarRead, HirEffectBits.None),

        // Writes
        HirInsnKind.SetIV => new HirEffect(HirEffectBits.None, HirEffectBits.IvarWrite),
        HirInsnKind.SetGV or HirInsnKind.SetSV => new HirEffect(HirEffectBits.None, HirEffectBits.GvarWrite),
        HirInsnKind.SetCV => new HirEffect(HirEffectBits.None, HirEffectBits.CvarWrite),
        HirInsnKind.SetConst or HirInsnKind.SetMCnst => new HirEffect(HirEffectBits.None, HirEffectBits.ConstWrite),
        HirInsnKind.SetUpVar => new HirEffect(HirEffectBits.None, HirEffectBits.UpVarWrite),

        // Arithmetic / comparison: may raise (TypeError, ZeroDiv...) but otherwise pure.
        HirInsnKind.Add or HirInsnKind.Sub or HirInsnKind.Mul or HirInsnKind.Div
            or HirInsnKind.AddI or HirInsnKind.SubI
            or HirInsnKind.Eq or HirInsnKind.Lt or HirInsnKind.Le or HirInsnKind.Gt or HirInsnKind.Ge
            => new(HirEffectBits.None, HirEffectBits.Raise),

        HirInsnKind.GetIdx or HirInsnKind.ARef => new HirEffect(HirEffectBits.IvarRead, HirEffectBits.Raise),
        HirInsnKind.SetIdx or HirInsnKind.ASet => new HirEffect(HirEffectBits.None, HirEffectBits.IvarWrite | HirEffectBits.Raise),

        // Control flow
        HirInsnKind.Jump or HirInsnKind.BranchIf or HirInsnKind.BranchUnless or HirInsnKind.BranchNil
            => Pure,
        HirInsnKind.Return or HirInsnKind.ReturnBlk or HirInsnKind.Break or HirInsnKind.Stop
            => new HirEffect(HirEffectBits.None, HirEffectBits.Return),
        HirInsnKind.RaiseIf => new HirEffect(HirEffectBits.None, HirEffectBits.Raise),

        // Calls — assume worst case
        HirInsnKind.Send or HirInsnKind.Super or HirInsnKind.Call => AnyCall,

        // Class machinery
        HirInsnKind.Enter => Pure,    // parameter binding only
        _ => AnyCall,                 // unknown / Other => conservative
    };
}
