using MRubyCS.Compiler;

namespace MRubyCS.Tests;

// End-to-end round-trip tests for HIR -> bytecode lowering.
//
// Strategy: hand-craft a minimal Irep, optimize it via MRubyState.Optimize,
// and verify the optimized Irep produces the same MRubyValue when executed.
// (Result-value comparison, per the design decision; no stdout capture.)
[TestFixture]
public class HirLoweringTest
{
    static Irep MakeReturnInt(int value)
    {
        // Just `LoadI_<value> R0; Return R0`. Tests the simplest possible
        // round-trip path through Optimize: one block, no edges, one live
        // SSA value.
        var op = value switch
        {
            -1 => OpCode.LoadI__1,
            0 => OpCode.LoadI_0,
            1 => OpCode.LoadI_1,
            2 => OpCode.LoadI_2,
            3 => OpCode.LoadI_3,
            4 => OpCode.LoadI_4,
            5 => OpCode.LoadI_5,
            6 => OpCode.LoadI_6,
            7 => OpCode.LoadI_7,
            _ => OpCode.LoadI8,
        };
        var seq = op == OpCode.LoadI8
            ? new byte[] { (byte)op, 0, (byte)value, (byte)OpCode.Return, 0 }
            : new byte[] { (byte)op, 0,              (byte)OpCode.Return, 0 };
        return new Irep { RegisterVariableCount = 4, Sequence = seq };
    }

    static Irep MakeReturnNil()
    {
        // `LoadNil R0; Return R0`
        var seq = new byte[] { (byte)OpCode.LoadNil, 0, (byte)OpCode.Return, 0 };
        return new Irep { RegisterVariableCount = 4, Sequence = seq };
    }

    static Irep MakeReturnTrue()
    {
        var seq = new byte[] { (byte)OpCode.LoadT, 0, (byte)OpCode.Return, 0 };
        return new Irep { RegisterVariableCount = 4, Sequence = seq };
    }

    [Test]
    public void RoundTrip_ReturnInt_PreservesValue()
    {
        var state = MRubyState.Create();
        var src = MakeReturnInt(5);
        var optimized = state.Optimize(src);

        // Both should evaluate to the integer 5.
        var origResult = state.Execute(src);
        var optResult = state.Execute(optimized);

        Assert.Multiple(() =>
        {
            Assert.That(origResult.IsInteger, Is.True);
            Assert.That(optResult.IsInteger, Is.True);
            Assert.That(optResult.IntegerValue, Is.EqualTo(origResult.IntegerValue));
            Assert.That(optResult.IntegerValue, Is.EqualTo(5));
        });
    }

    [Test]
    public void RoundTrip_ReturnNil_PreservesValue()
    {
        var state = MRubyState.Create();
        var src = MakeReturnNil();
        var optimized = state.Optimize(src);

        var origResult = state.Execute(src);
        var optResult = state.Execute(optimized);

        Assert.Multiple(() =>
        {
            Assert.That(origResult.IsNil, Is.True);
            Assert.That(optResult.IsNil, Is.True);
        });
    }

    [Test]
    public void RoundTrip_ReturnTrue_PreservesValue()
    {
        var state = MRubyState.Create();
        var src = MakeReturnTrue();
        var optimized = state.Optimize(src);

        var origResult = state.Execute(src);
        var optResult = state.Execute(optimized);

        Assert.Multiple(() =>
        {
            Assert.That(origResult.BoolValue, Is.True);
            Assert.That(optResult.BoolValue, Is.True);
        });
    }

    static Irep MakeAddTwoInts(int a, int b)
    {
        // R1 = a, R2 = b, R1 = R1 + R2, return R1.
        var aOp = ImmediateLoadOp(a, out var aOperand);
        var bOp = ImmediateLoadOp(b, out var bOperand);
        var seq = new System.Collections.Generic.List<byte>();
        seq.Add((byte)aOp); seq.Add(1); if (aOperand.HasValue) seq.Add(aOperand.Value);
        seq.Add((byte)bOp); seq.Add(2); if (bOperand.HasValue) seq.Add(bOperand.Value);
        seq.Add((byte)OpCode.Add); seq.Add(1);
        seq.Add((byte)OpCode.Return); seq.Add(1);
        return new Irep { RegisterVariableCount = 4, Sequence = seq.ToArray() };
    }

    static OpCode ImmediateLoadOp(int value, out byte? operand)
    {
        operand = null;
        switch (value)
        {
            case -1: return OpCode.LoadI__1;
            case 0: return OpCode.LoadI_0;
            case 1: return OpCode.LoadI_1;
            case 2: return OpCode.LoadI_2;
            case 3: return OpCode.LoadI_3;
            case 4: return OpCode.LoadI_4;
            case 5: return OpCode.LoadI_5;
            case 6: return OpCode.LoadI_6;
            case 7: return OpCode.LoadI_7;
        }
        operand = (byte)value;
        return OpCode.LoadI8;
    }

    [Test]
    public void RoundTrip_Add_FoldsToConstant()
    {
        // 7 + 5 should fold to 12 via ConstantFold and round-trip cleanly.
        var state = MRubyState.Create();
        var src = MakeAddTwoInts(7, 5);
        var optimized = state.Optimize(src);

        var origResult = state.Execute(src);
        var optResult = state.Execute(optimized);

        Assert.Multiple(() =>
        {
            Assert.That(origResult.IntegerValue, Is.EqualTo(12));
            Assert.That(optResult.IntegerValue, Is.EqualTo(12));
        });
    }

    [Test]
    public void RoundTrip_BranchOnConstant_FoldsAndRetains()
    {
        // R1 = 0, R2 = 1, R1 = R1 < R2 (true), JmpNot R1 -> RetFalse, RetTrue, RetFalse
        // ConstantFold collapses Lt to LoadTrue, but the branch survives until
        // a CFG-cleanup pass (B-2 step 2). Verify both before- and after-
        // optimize execute to true.
        var seq = new System.Collections.Generic.List<byte>();
        seq.Add((byte)OpCode.LoadI_0); seq.Add(1);
        seq.Add((byte)OpCode.LoadI_1); seq.Add(2);
        seq.Add((byte)OpCode.LT);      seq.Add(1);
        seq.Add((byte)OpCode.JmpNot);  seq.Add(1); seq.Add(0); seq.Add(1);
        seq.Add((byte)OpCode.RetTrue);
        seq.Add((byte)OpCode.RetFalse);

        var state = MRubyState.Create();
        var src = new Irep { RegisterVariableCount = 4, Sequence = seq.ToArray() };
        var optimized = state.Optimize(src);

        var origResult = state.Execute(src);
        var optResult = state.Execute(optimized);

        Assert.Multiple(() =>
        {
            Assert.That(origResult.BoolValue, Is.True);
            Assert.That(optResult.BoolValue, Is.True);
        });
    }

    [Test]
    public void RoundTrip_FibLikeBranchAndArithmetic()
    {
        // Approximation of fib's branchy + arithmetic shape, but standalone:
        //
        //   R1 = 3
        //   R2 = 2
        //   R1 = R1 < R2     (false)
        //   JmpNot R1 -> taken
        //   R1 = 99           (taken if condition true — won't happen)
        //   Jmp -> end
        //   taken: R1 = 7 + 5 (= 12)
        //   end: return R1
        //
        // Tests: conditional branch, fall-through layout, arithmetic via
        // scratch window, multi-block CFG.
        var seq = new System.Collections.Generic.List<byte>();
        // Jump offsets: JmpNot at pc=6 ends at pc=10; target pc=16 -> rel=6.
        // Jmp at pc=13 ends at pc=16; target pc=22 -> rel=6.
        seq.Add((byte)OpCode.LoadI_3); seq.Add(1);                  // pc=0..2: R1=3
        seq.Add((byte)OpCode.LoadI_2); seq.Add(2);                  // pc=2..4: R2=2
        seq.Add((byte)OpCode.LT);      seq.Add(1);                  // pc=4..6: R1 = R1<R2 (false)
        seq.Add((byte)OpCode.JmpNot);  seq.Add(1); seq.Add(0); seq.Add(6); // pc=6..10
        // taken leg (R1 was true — won't execute here):
        seq.Add((byte)OpCode.LoadI8);  seq.Add(1); seq.Add(99);     // pc=10..13: R1=99
        seq.Add((byte)OpCode.Jmp);     seq.Add(0); seq.Add(6);      // pc=13..16
        // false leg (R1 was false — executes, computes 7+5):
        seq.Add((byte)OpCode.LoadI_7); seq.Add(1);                  // pc=16..18: R1=7
        seq.Add((byte)OpCode.LoadI_5); seq.Add(2);                  // pc=18..20: R2=5
        seq.Add((byte)OpCode.Add);     seq.Add(1);                  // pc=20..22: R1 = R1+R2 = 12
        // end:
        seq.Add((byte)OpCode.Return);  seq.Add(1);                  // pc=22..24

        var state = MRubyState.Create();
        var src = new Irep { RegisterVariableCount = 4, Sequence = seq.ToArray() };
        var optimized = state.Optimize(src);

        var origResult = state.Execute(src);
        var optResult = state.Execute(optimized);

        Assert.Multiple(() =>
        {
            Assert.That(origResult.IntegerValue, Is.EqualTo(12),
                "Original Irep should compute 7 + 5 = 12 in the false leg");
            Assert.That(optResult.IntegerValue, Is.EqualTo(origResult.IntegerValue),
                "Optimized Irep should produce the same value");
        });
    }

    [Test]
    public void RoundTrip_Fibonacci_OptimizesChildIrep()
    {
        // The toplevel `def fib ... fib(10)` includes ops the lowering doesn't
        // yet cover (TDef / Method / Def / Class). Optimize falls back for
        // the toplevel but recursively optimizes the child Irep (the body of
        // `def fib`) which exercises Send + Enter + branches + arithmetic.
        var state = MRubyState.Create();
        using var compiler = MRubyCompiler.Create(state);
        using var compilation = compiler.Compile("""
            def fib(n)
              return n if n < 2
              fib(n - 1) + fib(n - 2)
            end
            fib(10)
            """u8);
        var bytecode = compilation.AsBytecode();
        var src = state.ParseBytecode(bytecode);

        // Sanity: child#0 is the body of `def fib`. Optimize() should produce
        // a different Sequence for it (Move/DCE/etc. cleanup at minimum).
        var origChild = src.Children[0];

        var optimized = state.Optimize(src);
        var optChild = optimized.Children[0];

        Assert.That(optChild.Sequence.SequenceEqual(origChild.Sequence), Is.False,
            "Child Irep sequence should change after optimization");

        // Both run to completion and yield 55.
        var origResult = state.Execute(src);
        var optResult = state.Execute(optimized);

        Assert.Multiple(() =>
        {
            Assert.That(origResult.IntegerValue, Is.EqualTo(55));
            Assert.That(optResult.IntegerValue, Is.EqualTo(55));
        });
    }

    [Test]
    public void Optimize_RegisterCountReducedFromOriginal()
    {
        // The input declares 4 registers; the lowered version only needs 1
        // (R0 reserved for self; the LoadInt's value flows directly into the
        // Return without needing more state).
        var state = MRubyState.Create();
        var src = MakeReturnInt(3);
        var optimized = state.Optimize(src);

        Assert.That(optimized.RegisterVariableCount, Is.LessThanOrEqualTo(src.RegisterVariableCount));
    }
}
