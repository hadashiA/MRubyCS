using System.Linq;
using MRubyCS.Hir;
using MRubyCS.Hir.Passes;

namespace MRubyCS.Tests;

[TestFixture]
public class HirPassesTest
{
    static Irep MakeUnusedLoads()
    {
        // R1 = 7         (unused)
        // R2 = 5         (unused)
        // R3 = nil
        // return R3
        // The two LoadInts are pure and unused — DCE must delete them.
        // (Arithmetic ops like Add carry a `may raise` effect bit and are
        // not removable by pure DCE without type-driven refinement.)
        var seq = new byte[]
        {
            (byte)OpCode.LoadI8, 1, 7,
            (byte)OpCode.LoadI8, 2, 5,
            (byte)OpCode.LoadNil, 3,
            (byte)OpCode.Return, 3,
        };
        return new Irep
        {
            RegisterVariableCount = 4,
            Sequence = seq,
        };
    }

    static Irep MakeSimpleAdd()
    {
        var seq = new byte[]
        {
            (byte)OpCode.LoadI8, 1, 7,
            (byte)OpCode.LoadI8, 2, 5,
            (byte)OpCode.Add,    1,
            (byte)OpCode.Return, 1,
        };
        return new Irep { RegisterVariableCount = 4, Sequence = seq };
    }

    [Test]
    public void HirVerifier_PassesFreshlyBuiltFunction()
    {
        var func = HirFunction.Build(MakeSimpleAdd());
        Assert.DoesNotThrow(() => HirVerifier.Verify(func));
    }

    [Test]
    public void HirVerifier_PassesAfterTypeInference()
    {
        var func = HirFunction.Build(MakeSimpleAdd());
        TypeInference.Run(func);
        Assert.DoesNotThrow(() => HirVerifier.Verify(func));
    }

    [Test]
    public void Dce_RemovesPureUnusedLoads()
    {
        var func = HirFunction.Build(MakeUnusedLoads());

        // Sanity: two LoadInts before DCE.
        var beforeLoadInt = func.Insns.Count(i => i.Kind == HirInsnKind.LoadInt);
        Assert.That(beforeLoadInt, Is.EqualTo(2), "Expected the lift to produce two LoadInts");

        var removed = Dce.Run(func);

        Assert.Multiple(() =>
        {
            Assert.That(removed, Is.GreaterThan(0), "DCE should remove at least the two LoadInts");
            var liveLoadInt = func.Insns.Count(i => i.Kind == HirInsnKind.LoadInt);
            Assert.That(liveLoadInt, Is.EqualTo(0), "LoadInts should have been killed");
            var entry = func[func.EntryBlock];
            Assert.That(entry.Insns.All(id => func[id].Kind != HirInsnKind.LoadInt), Is.True);
        });
        Assert.DoesNotThrow(() => HirVerifier.Verify(func));
    }

    [Test]
    public void Dce_RemovesUnusedParams()
    {
        // MakeSimpleAdd has RegisterVariableCount=4 but only uses R1 and R2.
        // The lifter emits one Param per register per block; the unused ones
        // (R0, R3) should be killed by DCE.
        var func = HirFunction.Build(MakeSimpleAdd());

        var liveParamsBefore = func.Insns.Count(i => i.Kind == HirInsnKind.Param);
        Assert.That(liveParamsBefore, Is.GreaterThanOrEqualTo(4));

        Dce.Run(func);

        var liveParamsAfter = func.Insns.Count(i => i.Kind == HirInsnKind.Param);
        Assert.That(liveParamsAfter, Is.LessThan(liveParamsBefore),
            "DCE should kill at least one unused Param");
        Assert.DoesNotThrow(() => HirVerifier.Verify(func));
    }

    [Test]
    public void Dce_KeepsLiveProducers()
    {
        var func = HirFunction.Build(MakeSimpleAdd());
        var removed = Dce.Run(func);

        // The Add result is consumed by Return, so it must stay.
        Assert.That(func.Insns.Any(i => i.Kind == HirInsnKind.Add), Is.True);
        Assert.That(removed, Is.GreaterThanOrEqualTo(0));
        Assert.DoesNotThrow(() => HirVerifier.Verify(func));
    }

    [Test]
    public void MoveElim_ReplacesMoveWithSource()
    {
        // R1 = 7
        // R2 = R1   (Move)
        // return R2
        var seq = new byte[]
        {
            (byte)OpCode.LoadI8, 1, 7,
            (byte)OpCode.Move,   2, 1,
            (byte)OpCode.Return, 2,
        };
        var irep = new Irep { RegisterVariableCount = 4, Sequence = seq };
        var func = HirFunction.Build(irep);
        TypeInference.Run(func);

        var rewritten = MoveElim.Run(func);
        Dce.Run(func);

        Assert.Multiple(() =>
        {
            Assert.That(rewritten, Is.GreaterThan(0));
            Assert.That(func.Insns.All(i => i.Kind != HirInsnKind.Move), Is.True,
                "All Move insns should have been rewritten + DCE'd");
            // The Return should now reference the LoadInt directly.
            var ret = func.Insns.First(i => i.Kind == HirInsnKind.Return);
            Assert.That(func[ret.Inputs[0]].Kind, Is.EqualTo(HirInsnKind.LoadInt));
        });
        Assert.DoesNotThrow(() => HirVerifier.Verify(func));
    }

    [Test]
    public void ConstantFold_FoldsAddOfTwoConstInts()
    {
        // R1 = 7
        // R2 = 5
        // R1 = R1 + R2
        // return R1
        var func = HirFunction.Build(MakeSimpleAdd());
        TypeInference.Run(func);

        var folded = ConstantFold.Run(func);
        Assert.That(folded, Is.GreaterThan(0));

        // The Add slot is now a LoadInt of 12.
        var loaded = func.Insns.Where(i => i.Kind == HirInsnKind.LoadInt).ToList();
        Assert.That(loaded.Any(i => i.Aux1 == 12), Is.True,
            "Expected a LoadInt 12 after folding 7 + 5");
        Assert.That(func.Insns.All(i => i.Kind != HirInsnKind.Add), Is.True,
            "Add slot should have been turned into LoadInt");

        // Run DCE — the now-unused LoadInt 7 / LoadInt 5 should be cleaned up.
        Dce.Run(func);

        Assert.DoesNotThrow(() => HirVerifier.Verify(func));
    }

    [Test]
    public void PhiSimplify_CollapsesSinglePredecessorParams()
    {
        // R1 = 0
        // R2 = 1
        // R1 = R1 < R2     (LT writes back into R1)
        // JmpNot R1 -> RetFalse leg
        // RetTrue
        // RetFalse
        var seq = new System.Collections.Generic.List<byte>();
        seq.Add((byte)OpCode.LoadI_0); seq.Add(1);
        seq.Add((byte)OpCode.LoadI_1); seq.Add(2);
        seq.Add((byte)OpCode.LT);      seq.Add(1);
        seq.Add((byte)OpCode.JmpNot);  seq.Add(1); seq.Add(0); seq.Add(1);
        seq.Add((byte)OpCode.RetTrue);
        seq.Add((byte)OpCode.RetFalse);

        var irep = new Irep { RegisterVariableCount = 4, Sequence = seq.ToArray() };
        var func = HirFunction.Build(irep);
        TypeInference.Run(func);

        // bb1 / bb2 each have a single predecessor (bb0). Their Params should
        // be collapsible. Before simplification, every block has one Param per
        // register, total RegisterCount * Blocks.
        var paramsBefore = func.Insns.Count(i => i.Kind == HirInsnKind.Param);
        var rewritten = PhiSimplify.Run(func);
        Assert.That(rewritten, Is.GreaterThan(0), "PhiSimplify should rewrite something");

        Dce.Run(func);

        var paramsAfter = func.Insns.Count(i => i.Kind == HirInsnKind.Param);
        Assert.That(paramsAfter, Is.LessThan(paramsBefore));
        Assert.DoesNotThrow(() => HirVerifier.Verify(func));
    }

    [Test]
    public void ConstantFold_FoldsComparison()
    {
        // R1 = 0
        // R2 = 1
        // R1 = R1 < R2     (LT writes back into R1)
        // return R1
        var seq = new byte[]
        {
            (byte)OpCode.LoadI_0, 1,
            (byte)OpCode.LoadI_1, 2,
            (byte)OpCode.LT,      1,
            (byte)OpCode.Return,  1,
        };
        var irep = new Irep { RegisterVariableCount = 4, Sequence = seq };
        var func = HirFunction.Build(irep);
        TypeInference.Run(func);

        var folded = ConstantFold.Run(func);
        Assert.That(folded, Is.GreaterThan(0));

        // Lt should have been folded into LoadTrue (0 < 1).
        Assert.That(func.Insns.Any(i => i.Kind == HirInsnKind.LoadTrue), Is.True);
        Assert.That(func.Insns.All(i => i.Kind != HirInsnKind.Lt), Is.True);
        Assert.DoesNotThrow(() => HirVerifier.Verify(func));
    }

    [Test]
    public void MakeEqualTo_RewritesAllUsersAndUseLists()
    {
        // R1 = 7
        // R2 = 5
        // R1 = R1 + R2
        // return R1
        var func = HirFunction.Build(MakeSimpleAdd());

        // Find the LoadInt 7 and LoadInt 5 SSA values.
        InsnId? load7 = null, load5 = null, addId = null;
        for (var i = 0; i < func.Insns.Count; i++)
        {
            var insn = func.Insns[i];
            if (insn.Kind == HirInsnKind.LoadInt && insn.Aux1 == 7) load7 = new InsnId(i);
            if (insn.Kind == HirInsnKind.LoadInt && insn.Aux1 == 5) load5 = new InsnId(i);
            if (insn.Kind == HirInsnKind.Add) addId = new InsnId(i);
        }
        Assert.That(load7, Is.Not.Null);
        Assert.That(load5, Is.Not.Null);
        Assert.That(addId, Is.Not.Null);

        // Pre-condition: load7 has at least one user (the Add).
        Assert.That(func.UsesOf(load7!.Value).Count, Is.GreaterThan(0));

        // Replace load7 with load5 across the function.
        func.MakeEqualTo(load7!.Value, load5!.Value);

        Assert.Multiple(() =>
        {
            // Add's first input now points at load5 instead of load7.
            Assert.That(func[addId!.Value].Inputs[0], Is.EqualTo(load5));
            // load7 has no remaining uses.
            Assert.That(func.UsesOf(load7!.Value).Count, Is.EqualTo(0));
        });
        Assert.DoesNotThrow(() => HirVerifier.Verify(func));
    }
}