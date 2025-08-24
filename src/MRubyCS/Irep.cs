using System.Runtime.CompilerServices;

namespace MRubyCS;

public enum CatchHandlerType : byte
{
    Rescue = 0,
    Ensure = 1,
    All = 2
}

public readonly struct CatchHandler(CatchHandlerType handlerType, uint begin, uint end, uint target)
{
    public readonly CatchHandlerType HandlerType = handlerType;

    /// <summary>
    /// The starting address to match the handler. Includes this.
    /// </summary>
    public readonly uint Begin = begin;

    /// <summary>
    /// The endpoint address that matches the handler. Not Includes this.
    /// </summary>
    public readonly uint End = end;

    /// <summary>
    /// The address to jump to if a match is made.
    /// </summary>
    public readonly uint Target = target;
}

/// <summary>
/// Program data
/// </summary>
public class Irep
{
    public byte Flags { get; init; }
    public ushort RegisterVariableCount { get; init; }
    public byte[] Sequence { get; init; } = [];
    public Symbol[] Symbols { get; init; } = [];
    public Symbol[] LocalVariables { get; init; } = [];
    public MRubyValue[] PoolValues { get; init; } = [];
    public Irep[] Children { get; init; } = [];
    public CatchHandler[] CatchHandlers { get; init; } = [];

    public bool TryFindCatchHandler(int pc, CatchHandlerType filter, out CatchHandler handler)
    {
        var noFilter = filter == CatchHandlerType.All;
        for (var i = CatchHandlers.Length - 1; i >= 0; i--)
        {
            var x = CatchHandlers[i];
            // The comparison operators use `>` and `<=` because pc already points to the next instruction
            if ((noFilter || x.HandlerType == filter) && pc > x.Begin && pc <= x.End)
            {
                handler = x;
                return true;
            }
        }
        handler = default;
        return false;
    }
}
