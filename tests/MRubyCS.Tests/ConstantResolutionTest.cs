using System.Linq;
using System.Text;
using MRubyCS.Compiler;
using MRubyCS.Hir;
using MRubyCS.Hir.Passes;

namespace MRubyCS.Tests;

[TestFixture]
public class ConstantResolutionTest
{
    static (Irep Irep, MRubyState State) CompileAndPredefine(string source, string predefSource)
    {
        var state = MRubyState.Create();
        var compiler = MRubyCompiler.Create(state);

        // Run the predefining script first so the constants are bound on
        // state.ObjectClass before Optimize sees the test program.
        if (!string.IsNullOrEmpty(predefSource))
        {
            using var pre = compiler.Compile(Encoding.UTF8.GetBytes(predefSource));
            state.LoadBytecode(pre.AsBytecode());
        }

        using var compilation = compiler.Compile(Encoding.UTF8.GetBytes(source));
        var irep = state.ParseBytecode(compilation.AsBytecode());
        compiler.Dispose();
        return (irep, state);
    }

    [Test]
    public void Resolves_KnownClassConstant()
    {
        // Pre-define `Vec` via a separate compile+execute step so it lives on
        // the state's ObjectClass at Optimize time. Then build a small Irep
        // that references `Vec` via GetConst and verify resolution.
        var (irep, state) = CompileAndPredefine(
            "Vec",
            "class Vec; end");
        var func = HirFunction.Build(irep);
        TypeInference.Run(func);

        var refined = ConstantResolution.Run(func, state);

        Assert.That(refined, Is.GreaterThan(0),
            "ConstantResolution should refine at least one GetConst");

        // The GetConst insn's type should be ExactClass with KnownClass=Vec.
        InsnId? getConstId = null;
        for (var i = 0; i < func.Insns.Count; i++)
        {
            if (func.Insns[i].Kind == HirInsnKind.GetConst)
            {
                getConstId = new InsnId(i);
                break;
            }
        }
        Assert.That(getConstId, Is.Not.Null);
        var t = func.TypeOf(getConstId!.Value);
        var vecSym = state.Intern("Vec"u8);
        var vecResolvedDirectly = state.TryGetConst(vecSym, out var vecValue) ? vecValue.Object as RClass : null;
        Assert.Multiple(() =>
        {
            Assert.That(t.Spec, Is.EqualTo(HirSpec.ExactClass));
            Assert.That(t.KnownClass, Is.Not.Null);
            Assert.That(t.KnownClass, Is.SameAs(vecResolvedDirectly),
                "HirType.KnownClass should match what state.TryGetConst returns");
            Assert.That(state.OptimizationInvariants.ConstantBindings.ContainsKey(vecSym), Is.True);
        });
    }

    [Test]
    public void Skips_UnknownConstant()
    {
        // Hand-craft an Irep that does GetConst on a symbol the state has
        // never bound. The pass should leave the GetConst at Any.
        var state = MRubyState.Create();
        var unknownSym = state.Intern("ZZZ_NotDefined"u8);
        var symIdx = 0;
        // Bytecode: GetConst R0, sym; Return R0; Stop.
        var seq = new byte[]
        {
            (byte)OpCode.GetConst, 0, (byte)symIdx,
            (byte)OpCode.Return, 0,
            (byte)OpCode.Stop,
        };
        var irep = new Irep
        {
            RegisterVariableCount = 4,
            Sequence = seq,
            Symbols = new[] { unknownSym },
        };

        var func = HirFunction.Build(irep);
        TypeInference.Run(func);
        var refined = ConstantResolution.Run(func, state);

        Assert.That(refined, Is.EqualTo(0),
            "Unknown constant should not be refined");
    }

    [Test]
    public void Optimize_PreservesResultWithKnownClass()
    {
        // End-to-end: a test that uses Vec but only as a Send target. The
        // Optimize() result should be value-equivalent to the original
        // even if ConstantResolution refined types.
        var state = MRubyState.Create();
        using var compiler = MRubyCompiler.Create(state);
        using var pre = compiler.Compile(Encoding.UTF8.GetBytes("class Vec; def self.id; 42; end; end"));
        state.LoadBytecode(pre.AsBytecode());

        using var prog = compiler.Compile(Encoding.UTF8.GetBytes("Vec.id"));
        var src = state.ParseBytecode(prog.AsBytecode());
        var optimized = state.Optimize(src);

        var origResult = state.Execute(src);
        var optResult = state.Execute(optimized);

        Assert.Multiple(() =>
        {
            Assert.That(origResult.IntegerValue, Is.EqualTo(42));
            Assert.That(optResult.IntegerValue, Is.EqualTo(42));
        });
    }
}
