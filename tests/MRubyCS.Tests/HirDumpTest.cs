using MRubyCS.Hir;

namespace MRubyCS.Tests;

// Tests for the HIR builder, dumper, and DumpHir convenience API. These
// construct synthetic Ireps in-process so they do not depend on the native
// mruby compiler dylib.
[TestFixture]
public class HirDumpTest
{
    static Irep MakeSimpleAdd()
    {
        // Bytecode equivalent of:
        //   R1 = 7
        //   R2 = 5
        //   R1 = R1 + R2     (OP_Add reads R[a] and R[a+1], writes R[a])
        //   return R1
        var iseq = new byte[]
        {
            (byte)OpCode.LoadI8, 1, 7,    // LoadI8 R1 = 7
            (byte)OpCode.LoadI8, 2, 5,    // LoadI8 R2 = 5
            (byte)OpCode.Add,    1,       // Add R1 = R1 + R2
            (byte)OpCode.Return, 1,       // Return R1
        };
        return new Irep
        {
            RegisterVariableCount = 4,
            Sequence = iseq,
        };
    }

    static Irep MakeSimpleIf()
    {
        // R1 = 0
        // R2 = 1
        // R3 = R1 < R2  (LT writes the boolean back into R1)
        // JmpNot R1 -> RetFalse leg
        // RetTrue
        // RetFalse
        //
        // mruby's LT is B-typed: [op][a]. It reads R[a] and R[a+1], writes R[a].
        // JmpNot is BS: [op][a][hi][lo] with rel target after the op.
        //
        // We assemble byte offsets manually.
        var seq = new System.Collections.Generic.List<byte>();
        seq.Add((byte)OpCode.LoadI_0); seq.Add(1);                  // R1=0  @0..2
        seq.Add((byte)OpCode.LoadI_1); seq.Add(2);                  // R2=1  @2..4
        seq.Add((byte)OpCode.LT);      seq.Add(1);                  // R1=R1<R2 @4..6
        // JmpNot R1, +relative: 4 bytes total. Skip RetTrue (1 byte). target = pc + 4 + rel.
        // We want to land on RetFalse at offset (current end + 1 byte for RetTrue).
        seq.Add((byte)OpCode.JmpNot); seq.Add(1);
        // 16-bit BE relative: skip past JmpNot end (4 bytes after start) + RetTrue (1 byte) = +1.
        seq.Add(0); seq.Add(1);                                     // @6..10
        seq.Add((byte)OpCode.RetTrue);                              // @10..11
        seq.Add((byte)OpCode.RetFalse);                             // @11..12

        return new Irep
        {
            RegisterVariableCount = 4,
            Sequence = seq.ToArray(),
        };
    }

    [Test]
    public void DumpSimpleAdd()
    {
        var irep = MakeSimpleAdd();
        var func = HirFunction.Build(irep);
        TypeInferenceRun(func);
        var dump = HirDumper.Dump(func, new HirDumpOptions { ShowTypes = true });
        TestContext.Out.WriteLine(dump);

        Assert.Multiple(() =>
        {
            Assert.That(dump, Does.Contain("LoadInt 7"));
            Assert.That(dump, Does.Contain("LoadInt 5"));
            Assert.That(dump, Does.Contain("Add"));
            Assert.That(dump, Does.Contain("Integer=7"));
            Assert.That(dump, Does.Contain("Integer=5"));
            Assert.That(dump, Does.Contain("Return"));
        });
    }

    [Test]
    public void DumpDiamondCfg()
    {
        var irep = MakeSimpleIf();
        var func = HirFunction.Build(irep);

        Assert.Multiple(() =>
        {
            Assert.That(func.Blocks.Count, Is.GreaterThanOrEqualTo(3),
                "Expected entry + true leg + false leg blocks");
            Assert.That(func.Edges.Count, Is.GreaterThanOrEqualTo(2),
                "Branch should produce taken + fallthrough edges");
        });

        TypeInferenceRun(func);
        var dump = HirDumper.Dump(func, new HirDumpOptions { ShowTypes = true });
        TestContext.Out.WriteLine(dump);

        Assert.That(dump, Does.Contain("BranchUnless"));
        Assert.That(dump, Does.Contain("Bool"));
    }

    [Test]
    public void DumpHirApi()
    {
        // The MRubyState.DumpHir convenience method should produce a string that
        // includes the section header and walks child Ireps recursively.
        var state = MRubyState.Create();
        var parent = MakeSimpleAdd();

        var output = state.DumpHir(parent);
        TestContext.Out.WriteLine(output);

        Assert.Multiple(() =>
        {
            Assert.That(output, Does.Contain("=== HIR <top> ==="));
            Assert.That(output, Does.Contain("LoadInt 7"));
            Assert.That(output, Does.Contain("Return"));
        });
    }

    [Test]
    public void EmptyIrepIsHandled()
    {
        var irep = new Irep { RegisterVariableCount = 0, Sequence = System.Array.Empty<byte>() };
        var func = HirFunction.Build(irep);
        Assert.That(func.Blocks.Count, Is.EqualTo(1));
        Assert.That(func.Insns.Count, Is.EqualTo(0));
    }

    static void TypeInferenceRun(HirFunction func) => MRubyCS.Hir.Passes.TypeInference.Run(func);
}
