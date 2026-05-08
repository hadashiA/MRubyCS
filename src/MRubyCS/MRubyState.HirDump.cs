using System.IO;
using System.Text;
using MRubyCS.Hir;
using MRubyCS.Hir.Passes;

namespace MRubyCS;

partial class MRubyState
{
    /// <summary>
    /// Build HIR for the given Irep and return its text dump. Walks child Ireps
    /// recursively. Suitable for diagnostics and exploring the IR shape.
    /// </summary>
    public string DumpHir(Irep irep, HirDumpOptions? options = null, bool runTypeInference = true)
    {
        var sb = new StringBuilder();
        DumpHir(irep, sb, options, runTypeInference);
        return sb.ToString();
    }

    public void DumpHir(Irep irep, TextWriter writer, HirDumpOptions? options = null, bool runTypeInference = true)
    {
        var sb = new StringBuilder();
        DumpHir(irep, sb, options, runTypeInference);
        writer.Write(sb.ToString());
    }

    public void DumpHir(Irep irep, StringBuilder sb, HirDumpOptions? options = null, bool runTypeInference = true)
    {
        var opts = options ?? new HirDumpOptions { State = this, ShowTypes = runTypeInference };
        if (opts.State == null)
        {
            opts = new HirDumpOptions
            {
                State = this,
                ShowTypes = opts.ShowTypes,
                ShowEffects = opts.ShowEffects,
                ShowEdgeArgs = opts.ShowEdgeArgs,
                ShowParams = opts.ShowParams,
                ShowSourcePc = opts.ShowSourcePc,
                ShowDeadParams = opts.ShowDeadParams,
                RunOptimizations = opts.RunOptimizations,
            };
        }

        DumpHirRecursive(irep, sb, opts, runTypeInference, depth: 0, label: "<top>");
    }

    static void DumpHirRecursive(Irep irep, StringBuilder sb, HirDumpOptions opts, bool runTypeInference, int depth, string label)
    {
        var func = HirFunction.Build(irep);
        if (runTypeInference) TypeInference.Run(func);

        if (opts.RunOptimizations && runTypeInference)
        {
            MoveElim.Run(func);
            ConstantFold.Run(func);
            PhiSimplify.Run(func);
            Dce.Run(func);
        }

        if (depth > 0) sb.Append('\n');
        sb.Append("=== HIR ").Append(label).Append(" ===\n");
        HirDumper.Write(new StringWriter(sb), func, opts);

        for (var i = 0; i < irep.Children.Length; i++)
        {
            DumpHirRecursive(irep.Children[i], sb, opts, runTypeInference, depth + 1,
                label: $"{label}/child#{i}");
        }
    }
}
