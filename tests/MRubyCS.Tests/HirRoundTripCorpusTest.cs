using System.Text;
using MRubyCS.Compiler;

namespace MRubyCS.Tests;

// Corpus-driven round-trip validation for MRubyState.Optimize.
//
// Each test case is a small Ruby program with a deterministic expected
// result (typically an integer). We compile the source once via
// MRubyCompiler, then run two paths:
//
//   1. The original parsed Irep -> Execute: observed as `origResult`.
//   2. Optimize(Irep) -> Execute: observed as `optResult`.
//
// Both must equal the expected value. The harness creates a fresh
// MRubyState per case to avoid leakage between scripts (constants /
// method definitions / etc.).
//
// Idempotence: running Optimize twice on the same Irep must yield
// equivalent bytecode (no further changes after the first pass at the
// HIR level under our current set of cleanup passes). This catches
// passes that mutate state-they-claim-to-leave-alone.
[TestFixture]
public class HirRoundTripCorpusTest
{
    public record Case(string Label, string Source, long Expected);

    static readonly Case[] Corpus =
    [
        new("int literal", "42", 42),
        new("simple add", "1 + 2", 3),
        new("chain add", "1 + 2 + 3 + 4", 10),
        new("subtract chain", "10 - 3 - 2", 5),
        new("identity method",
            """
            def id(x) x end
            id(42)
            """, 42),
        new("two-arg add",
            """
            def add(a, b) a + b end
            add(7, 8)
            """, 15),
        new("three-arg sum",
            """
            def sum3(a, b, c) a + b + c end
            sum3(1, 2, 3)
            """, 6),
        new("conditional return",
            """
            def cond(x)
              if x > 0
                1
              else
                -1
              end
            end
            cond(5)
            """, 1),
        new("conditional branch (negative)",
            """
            def cond(x)
              if x > 0
                1
              else
                -1
              end
            end
            cond(-3)
            """, -1),
        new("recursive fib",
            """
            def fib(n)
              return n if n < 2
              fib(n - 1) + fib(n - 2)
            end
            fib(10)
            """, 55),
        new("recursive factorial",
            """
            def fact(n)
              return 1 if n <= 1
              n * fact(n - 1)
            end
            fact(5)
            """, 120),
        new("nested calls",
            """
            def dbl(x) x + x end
            def quad(x) dbl(dbl(x)) end
            quad(3)
            """, 12),
    ];

    [TestCaseSource(nameof(Corpus))]
    public void Optimize_PreservesResult(Case c)
    {
        var state = MRubyState.Create();
        using var compiler = MRubyCompiler.Create(state);
        using var compilation = compiler.Compile(Encoding.UTF8.GetBytes(c.Source));
        var src = state.ParseBytecode(compilation.AsBytecode());
        var optimized = state.Optimize(src);

        var origResult = state.Execute(src);
        var optResult = state.Execute(optimized);

        Assert.Multiple(() =>
        {
            Assert.That(origResult.IntegerValue, Is.EqualTo(c.Expected),
                $"original result for '{c.Label}'");
            Assert.That(optResult.IntegerValue, Is.EqualTo(c.Expected),
                $"optimized result for '{c.Label}'");
        });
    }

    [TestCaseSource(nameof(Corpus))]
    public void Optimize_IsIdempotent(Case c)
    {
        // Optimize(Optimize(src)).Sequence should equal Optimize(src).Sequence
        // for every Irep in the tree. Re-running the cleanup passes on already-
        // cleaned HIR must not change anything observable.
        var state = MRubyState.Create();
        using var compiler = MRubyCompiler.Create(state);
        using var compilation = compiler.Compile(Encoding.UTF8.GetBytes(c.Source));
        var src = state.ParseBytecode(compilation.AsBytecode());

        var once = state.Optimize(src);
        var twice = state.Optimize(once);

        AssertIrepEquivalent(once, twice, c.Label);
    }

    static void AssertIrepEquivalent(Irep a, Irep b, string label, string path = "<top>")
    {
        Assert.That(b.Sequence, Is.EqualTo(a.Sequence),
            $"[{label}] {path} sequence differs after second Optimize");
        Assert.That(b.RegisterVariableCount, Is.EqualTo(a.RegisterVariableCount),
            $"[{label}] {path} register count differs");
        Assert.That(b.Children.Length, Is.EqualTo(a.Children.Length),
            $"[{label}] {path} children count differs");
        for (var i = 0; i < a.Children.Length; i++)
        {
            AssertIrepEquivalent(a.Children[i], b.Children[i], label, $"{path}/child#{i}");
        }
    }

    [Test]
    public void Coverage_Snapshot()
    {
        // Diagnostic — counts how many Ireps in the corpus get fully lowered
        // (sequence changes after Optimize) vs. fall back to the original
        // sequence (lowering hit an unsupported insn). Useful as a coverage
        // signal as we widen H3+ insn support.
        var optimizedCount = 0;
        var fallbackCount = 0;
        foreach (var c in Corpus)
        {
            var state = MRubyState.Create();
            using var compiler = MRubyCompiler.Create(state);
            using var compilation = compiler.Compile(Encoding.UTF8.GetBytes(c.Source));
            var src = state.ParseBytecode(compilation.AsBytecode());
            var optimized = state.Optimize(src);
            CountIreps(src, optimized, ref optimizedCount, ref fallbackCount);
        }
        TestContext.Out.WriteLine(
            $"Corpus coverage: {optimizedCount} Ireps lowered, {fallbackCount} fell back to original");
        Assert.That(optimizedCount, Is.GreaterThan(0),
            "expected at least some Ireps in the corpus to have been lowered");
    }

    static void CountIreps(Irep orig, Irep opt, ref int optimized, ref int fallback)
    {
        if (!orig.Sequence.AsSpan().SequenceEqual(opt.Sequence)) optimized++;
        else fallback++;
        for (var i = 0; i < orig.Children.Length && i < opt.Children.Length; i++)
        {
            CountIreps(orig.Children[i], opt.Children[i], ref optimized, ref fallback);
        }
    }
}
