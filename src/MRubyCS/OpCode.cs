namespace MRubyCS;

/// <summary>
/// </summary>
/// <remarks>
/// https://github.com/mruby/mruby/blob/master/doc/internal/opcode.md
/// </remarks>
// ReSharper disable InconsistentNaming
public enum OpCode : byte
{
    Nop = 0,
    Move = 1,
    LoadL = 2,
    LoadI8 = 3,
    LoadINeg = 4,
    LoadI__1 = 5,
    LoadI_0 = 6,
    LoadI_1 = 7,
    LoadI_2 = 8,
    LoadI_3= 9 ,
    LoadI_4 = 10,
    LoadI_5 = 11,
    LoadI_6 = 12,
    LoadI_7= 13,
    LoadI16 = 14,
    LoadI32 = 15,
    LoadSym = 16,
    LoadNil = 17,
    LoadSelf = 18,
    LoadT = 19,
    LoadF = 20,
    GetGV = 21,
    SetGV = 22,
    GetSV = 23,
    SetSV = 24,
    GetIV = 25,
    SetIV = 26,
    GetCV = 27,
    SetCV = 28,
    GetConst = 29,
    SetConst = 30,
    GetMCnst = 31,
    SetMCnst = 32,
    GetUpVar = 33,
    SetUpVar = 34,
    GetIdx = 35,
    SetIdx = 36,
    Jmp = 37,
    JmpIf = 38,
    JmpNot = 39,
    JmpNil = 40,
    JmpUw = 41,
    Except = 42,
    Rescue = 43,
    RaiseIf = 44,
    SSend = 45,
    SSendB = 46,
    Send = 47,
    SendB = 48,
    Call = 49,
    Super = 50,
    ArgAry = 51,
    Enter = 52,
    KeyP = 53,
    KeyEnd = 54,
    KArg = 55,
    Return = 56,
    ReturnBlk = 57,
    Break = 58,
    BlkPush = 59,
    Add = 60,
    AddI = 61,
    Sub = 62,
    SubI = 63,
    Mul = 64,
    Div = 65,
    EQ = 66,
    LT = 67,
    LE = 68,
    GT = 69,
    GE = 70,
    Array = 71,
    Array2 = 72,
    AryCat = 73,
    AryPush = 74,
    ArySplat = 75,
    ARef = 76,
    ASet = 77,
    APost = 78,
    Intern = 79,
    Symbol = 80,
    String = 81,
    StrCat = 82,
    Hash = 83,
    HashAdd = 84,
    HashCat = 85,
    Lambda = 86,
    Block = 87,
    Method = 88,
    RangeInc = 89,
    RangeExc = 90,
    OClass = 91,
    Class = 92,
    Module = 93,
    Exec = 94,
    Def = 95,
    Alias = 96,
    Undef = 97,
    SClass = 98,
    TClass = 99,
    Debug,
    Err = 101,
    EXT1, // not implemented
    EXT2, // not implemented
    EXT3, // not implemented
    Stop = 105,

    // use internally
    SendInternal = 255,
}
