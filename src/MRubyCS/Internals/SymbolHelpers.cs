namespace MRubyCS.Internals;

internal static class SymbolHelpers
{

    static readonly  Symbol[] OpCodeSymbols =
    [
        Names.OpAdd,
        Names.OpAdd,
        Names.OpSub,
        Names.OpSub,
        Names.OpMul,
        Names.OpDiv,
        Names.OpEq,
        Names.OpLt,
        Names.OpLe,
        Names.OpGt,
        Names.OpGe
    ];

    public static Symbol GetOpCodeSymbol(OpCode opCode)
    {
        return opCode == OpCode.GetIdx ? Names.OpAref : OpCodeSymbols[(int)opCode-(int)OpCode.Add];
    }
}