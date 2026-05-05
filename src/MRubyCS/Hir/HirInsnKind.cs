namespace MRubyCS.Hir;

// What an Insn does. Insn is a single C# class for all kinds; this enum is the
// discriminator. Operand layout (which Inputs[i] means what) and which Aux
// fields are populated is a function of Kind — see HirInsnSemantics.cs and the
// dumper for the per-Kind contracts.
public enum HirInsnKind
{
    // Control / pseudo
    Param,         // block parameter (incoming φ position). No inputs. Aux1 = parameter slot index.
    Unknown,       // bytecode op we have not lifted yet
    Nop,

    // Constants / immediates
    LoadNil,
    LoadTrue,
    LoadFalse,
    LoadSelf,
    LoadInt,       // Aux1 = i32 literal
    LoadSym,       // AuxSymbol
    LoadPool,      // Aux1 = pool index, AuxObj = MRubyValue (boxed) for direct inspection
    LoadString,    // Aux1 = pool index, AuxObj = pooled RString reference

    // Register move (mostly redundant after SSA construction; kept for round-tripping)
    Move,

    // Variable access
    GetGV, SetGV,
    GetIV, SetIV,
    GetCV, SetCV,
    GetSV, SetSV,
    GetConst, SetConst,
    GetMCnst, SetMCnst,
    GetUpVar, SetUpVar,    // Aux1 = slot, Aux2 = up-level

    // Arithmetic / comparison (untyped at lift time; type-spec pass narrows)
    Add, Sub, Mul, Div,
    AddI, SubI,            // Aux1 = immediate
    Eq, Lt, Le, Gt, Ge,

    // Indexing
    GetIdx, SetIdx,
    ARef, ASet,

    // Allocation
    NewArray,              // Aux1 = element count; inputs are the elements
    NewHash,               // Aux1 = pair count; inputs are k0,v0,k1,v1,...
    NewString,             // Aux1 = pool index
    NewRange,              // AuxBool = exclusive
    Lambda,                // Aux1 = child Irep index
    Block,                 // Aux1 = child Irep index
    Method,                // Aux1 = child Irep index

    // Method dispatch
    // Send: AuxSymbol = method id, Aux1 = argc, Aux2 = kargc,
    //       AuxBool = has-block, AuxBool2 = self-send.
    //       Inputs layout: [recv?] [args...] [k0,v0,...] [block?]
    Send,
    Super,
    Call,                  // OP_CALL on a Proc

    // Control flow (block terminators only)
    Jump,                  // BranchEdge index in OutEdges
    BranchIf,              // input is the predicate; true edge first, false edge second
    BranchUnless,
    BranchNil,
    Return,                // input is the return value
    ReturnBlk,
    Break,
    Stop,
    RaiseIf,

    // Other / not yet lifted
    Enter,
    Other,
}
