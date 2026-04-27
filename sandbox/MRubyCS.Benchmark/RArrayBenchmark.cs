using BenchmarkDotNet.Attributes;

namespace MRubyCS.Benchmark;

[Config(typeof(BenchmarkConfig))]
[MemoryDiagnoser]
public class RArrayBenchmark
{
    const int N = 1000;

    MRubyState state = null!;
    MRubyValue[] fixnumValues = null!;
    MRubyValue[] floatValues = null!;
    MRubyValue[] mixedValues = null!;
    RArray fixnumArray = null!;
    RArray floatArray = null!;
    RArray mixedArray = null!;

    [GlobalSetup]
    public void Setup()
    {
        state = MRubyState.Create();

        fixnumValues = new MRubyValue[N];
        floatValues = new MRubyValue[N];
        mixedValues = new MRubyValue[N];
        for (var i = 0; i < N; i++)
        {
            fixnumValues[i] = new MRubyValue(i);
            floatValues[i] = new MRubyValue(i * 1.5);
            // Half fixnum, half symbol -> always mixed
            mixedValues[i] = (i % 2 == 0)
                ? new MRubyValue(i)
                : new MRubyValue(new Symbol((uint)(i + 1)));
        }

        fixnumArray = state.NewArray(fixnumValues);
        floatArray = state.NewArray(floatValues);
        mixedArray = state.NewArray(mixedValues);
    }

    // ---- Construction ----

    [Benchmark]
    public RArray ConstructFromFixnumSpan() => state.NewArray(fixnumValues);

    [Benchmark]
    public RArray ConstructFromFloatSpan() => state.NewArray(floatValues);

    [Benchmark]
    public RArray ConstructFromMixedSpan() => state.NewArray(mixedValues);

    // ---- Push ----

    [Benchmark]
    public RArray PushFixnums()
    {
        var arr = state.NewArray(0);
        for (var i = 0; i < N; i++)
        {
            arr.Push(new MRubyValue(i));
        }
        return arr;
    }

    // ---- Read ----

    [Benchmark]
    public long IndexerGetSum()
    {
        long sum = 0;
        var arr = fixnumArray;
        for (var i = 0; i < N; i++)
        {
            sum += arr[i].FixnumValue;
        }
        return sum;
    }

    [Benchmark]
    public long IterateSum()
    {
        long sum = 0;
        foreach (var v in fixnumArray)
        {
            sum += v.FixnumValue;
        }
        return sum;
    }

    // ---- Mutation through indexer (rewrite same values) ----

    [Benchmark]
    public RArray IndexerSet()
    {
        // Re-build fresh each invocation so we always exercise the specialized
        // write path (the input is already specialized at construct-time).
        var arr = state.NewArray(fixnumValues);
        for (var i = 0; i < N; i++)
        {
            arr[i] = new MRubyValue(i + 1);
        }
        return arr;
    }

    // ---- Demotion cost: AsSpan() on a specialized array materializes MRubyValue[] ----

    [Benchmark]
    public int AsSpanAfterFixnumConstruct()
    {
        var arr = state.NewArray(fixnumValues);
        var span = arr.AsSpan();
        return span.Length;
    }
}
