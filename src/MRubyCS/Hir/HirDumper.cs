using System.Globalization;
using System.IO;
using System.Text;

namespace MRubyCS.Hir;

public sealed class HirDumpOptions
{
    public bool ShowParams { get; init; } = true;
    public bool ShowEdgeArgs { get; init; } = true;
    public bool ShowTypes { get; init; } = false;
    public bool ShowEffects { get; init; } = false;
    public bool ShowSourcePc { get; init; } = true;
    public bool ShowDeadParams { get; init; } = false;  // Suppress all-bb-have-N-params noise
    // When true, DumpHir applies the structurally-safe optimization passes
    // (MoveElim / ConstantFold / Dce) before rendering. Useful for visually
    // diffing the IR before vs. after optimization. Off by default so the
    // dump represents the raw lifted form.
    public bool RunOptimizations { get; init; } = false;
    public MRubyState? State { get; init; }
}

// Pretty-print a HirFunction. Format roughly:
//
//   func irep (registers=4, blocks=3, insns=27)
//   bb0 [0000..000d):
//     params: v0, v1, v2, v3
//     v4 = LoadInt 10               ; @0000 LoadI8 [Integer=10]
//     v5 = Send v4 :fib argc=1 self ; @0003 SSend
//     Return v5                     ; @000b Return
//     -> bb1 [v0, v1, v2, v5]
//
public static class HirDumper
{
    public static string Dump(HirFunction func, HirDumpOptions? options = null)
    {
        var sb = new StringBuilder();
        Write(sb, func, options ?? new HirDumpOptions());
        return sb.ToString();
    }

    public static void Write(TextWriter writer, HirFunction func, HirDumpOptions? options = null)
    {
        var sb = new StringBuilder();
        Write(sb, func, options ?? new HirDumpOptions());
        writer.Write(sb.ToString());
    }

    static void Write(StringBuilder sb, HirFunction func, HirDumpOptions opt)
    {
        sb.AppendFormat(CultureInfo.InvariantCulture,
            "func irep (registers={0}, blocks={1}, insns={2})\n",
            func.RegisterCount, func.Blocks.Count, func.Insns.Count);

        foreach (var block in func.Blocks)
        {
            WriteBlock(sb, func, block, opt);
            sb.Append('\n');
        }
    }

    static void WriteBlock(StringBuilder sb, HirFunction func, HirBlock block, HirDumpOptions opt)
    {
        sb.Append(block).Append(':');
        if (block.InEdges.Count > 0)
        {
            sb.Append("  ; preds=");
            for (var i = 0; i < block.InEdges.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(block.InEdges[i].Source);
            }
        }
        sb.Append('\n');

        if (opt.ShowParams && (opt.ShowDeadParams || HasInterestingParams(func, block)))
        {
            sb.Append("  params:");
            for (var i = 0; i < block.Params.Count; i++)
            {
                sb.Append(' ').Append(block.Params[i]);
                if (opt.ShowTypes) sb.Append(':').Append(func.TypeOf(block.Params[i]));
            }
            sb.Append('\n');
        }

        foreach (var id in block.Insns)
        {
            WriteInsn(sb, func, id, opt);
        }

        // Outgoing edges
        if (block.OutEdges.Count > 0)
        {
            for (var i = 0; i < block.OutEdges.Count; i++)
            {
                var e = block.OutEdges[i];
                sb.Append("  -> ").Append(e.Target);
                if (opt.ShowEdgeArgs && e.Args.Count > 0)
                {
                    sb.Append(" [");
                    for (var j = 0; j < e.Args.Count; j++)
                    {
                        if (j > 0) sb.Append(", ");
                        sb.Append(e.Args[j]);
                    }
                    sb.Append(']');
                }
                sb.Append('\n');
            }
        }
    }

    // For most blocks, params are RegisterCount placeholders that no analyses
    // care about until DCE runs. Default to suppressing them; user can enable
    // via ShowDeadParams.
    static bool HasInterestingParams(HirFunction func, HirBlock block) => false;

    static void WriteInsn(StringBuilder sb, HirFunction func, InsnId id, HirDumpOptions opt)
    {
        var insn = func[id];
        sb.Append("  ");
        if (insn.HasOutput)
        {
            sb.Append(id);
            if (opt.ShowTypes) sb.Append(':').Append(func.TypeOf(id));
            sb.Append(" = ");
        }
        sb.Append(insn.Kind.ToString());

        if (insn.Inputs.Count > 0)
        {
            sb.Append(' ');
            for (var i = 0; i < insn.Inputs.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(insn.Inputs[i]);
            }
        }
        WriteAux(sb, insn, opt);

        if (opt.ShowSourcePc)
        {
            sb.Append("\t; @").AppendFormat(CultureInfo.InvariantCulture, "{0:x4} ", insn.SourcePc).Append(insn.SourceOpCode);
        }
        if (opt.ShowEffects)
        {
            var e = func.EffectOf(id);
            if (!e.IsPure) sb.Append(" effect=").Append(e);
        }
        sb.Append('\n');
    }

    static void WriteAux(StringBuilder sb, HirInsn insn, HirDumpOptions opt)
    {
        switch (insn.Kind)
        {
            case HirInsnKind.LoadInt:
            case HirInsnKind.AddI: case HirInsnKind.SubI:
                sb.Append(' ').Append(insn.Aux1);
                break;
            case HirInsnKind.LoadSym:
            case HirInsnKind.GetIV: case HirInsnKind.SetIV:
            case HirInsnKind.GetGV: case HirInsnKind.SetGV:
            case HirInsnKind.GetCV: case HirInsnKind.SetCV:
            case HirInsnKind.GetConst: case HirInsnKind.SetConst:
            case HirInsnKind.GetMCnst: case HirInsnKind.SetMCnst:
                sb.Append(' ');
                AppendSym(sb, insn.AuxSymbol, opt.State);
                break;
            case HirInsnKind.LoadPool:
            case HirInsnKind.NewString:
                sb.Append(" pool=").Append(insn.Aux1);
                if (insn.AuxObj is MRubyValue mv) sb.Append('(').Append(mv).Append(')');
                break;
            case HirInsnKind.GetUpVar:
            case HirInsnKind.SetUpVar:
                sb.Append(" idx=").Append(insn.Aux1).Append(" up=").Append(insn.Aux2);
                break;
            case HirInsnKind.NewArray: case HirInsnKind.NewHash:
                sb.Append(" n=").Append(insn.Aux1);
                break;
            case HirInsnKind.Send:
                sb.Append(' ');
                AppendSym(sb, insn.AuxSymbol, opt.State);
                sb.Append(" argc=").Append(insn.Aux1);
                if (insn.Aux2 > 0) sb.Append(" kargc=").Append(insn.Aux2);
                if (insn.AuxBool) sb.Append(" &block");
                if (insn.AuxBool2) sb.Append(" self");
                break;
            case HirInsnKind.Lambda: case HirInsnKind.Block: case HirInsnKind.Method:
                sb.Append(" child=").Append(insn.Aux1);
                break;
            case HirInsnKind.Jump:
            case HirInsnKind.BranchIf:
            case HirInsnKind.BranchUnless:
            case HirInsnKind.BranchNil:
                sb.AppendFormat(CultureInfo.InvariantCulture, " ->@{0:x4}", insn.Aux1);
                break;
            case HirInsnKind.Return:
                if (insn.AuxObj is string s) sb.Append(' ').Append(s);
                else if (insn.AuxBool) sb.Append(" self");
                break;
            case HirInsnKind.Param:
                sb.Append(" R").Append(insn.Aux1);
                break;
        }
    }

    static void AppendSym(StringBuilder sb, Symbol sym, MRubyState? state)
    {
        if (state != null && sym.Value != 0)
        {
            try
            {
                sb.Append(':').Append(state.NameOf(sym).ToString());
                return;
            }
            catch { }
        }
        sb.Append("sym#").Append(sym.Value);
    }
}
