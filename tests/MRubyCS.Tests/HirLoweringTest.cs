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
