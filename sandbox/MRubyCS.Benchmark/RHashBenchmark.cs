using BenchmarkDotNet.Attributes;

namespace MRubyCS.Benchmark;

[Config(typeof(BenchmarkConfig))]
[MemoryDiagnoser]
public class RHashBenchmark
{
    const int N = 1000;

    MRubyState state = null!;
    MRubyValue[] symbolKeys = null!;
    MRubyValue[] mixedKeys = null!;
    MRubyValue[] payload = null!;
    RHash symbolKeyedHash = null!;
    RHash mixedKeyedHash = null!;

    [GlobalSetup]
    public void Setup()
    {
        state = MRubyState.Create();

        symbolKeys = new MRubyValue[N];
        mixedKeys = new MRubyValue[N];
        payload = new MRubyValue[N];
        for (var i = 0; i < N; i++)
        {
            // Distinct interned symbols. Use long names so they don't all
            // collapse into the same inline-packed value (when that lands).
            var sym = state.Intern($"key_{i:D5}");
            symbolKeys[i] = new MRubyValue(sym);
            payload[i] = new MRubyValue(i);

            // Half symbols, half integers -> always mixed.
            mixedKeys[i] = (i % 2 == 0)
                ? new MRubyValue(sym)
                : new MRubyValue(i);
        }

        symbolKeyedHash = state.NewHash(N);
        mixedKeyedHash = state.NewHash(N);
        for (var i = 0; i < N; i++)
        {
            symbolKeyedHash[symbolKeys[i]] = payload[i];
            mixedKeyedHash[mixedKeys[i]] = payload[i];
        }
    }

    // ---- Build ----

    [Benchmark]
    public RHash BuildSymbolKeyed()
    {
        var h = state.NewHash(N);
        for (var i = 0; i < N; i++)
        {
            h[symbolKeys[i]] = payload[i];
        }
        return h;
    }

    [Benchmark]
    public RHash BuildMixedKeys()
    {
        var h = state.NewHash(N);
        for (var i = 0; i < N; i++)
        {
            h[mixedKeys[i]] = payload[i];
        }
        return h;
    }

    // ---- Lookup ----

    [Benchmark]
    public long LookupSymbolKeys()
    {
        long sum = 0;
        var h = symbolKeyedHash;
        for (var i = 0; i < N; i++)
        {
            if (h.TryGetValue(symbolKeys[i], out var v))
            {
                sum += v.FixnumValue;
            }
        }
        return sum;
    }

    [Benchmark]
    public long LookupMixedKeys()
    {
        long sum = 0;
        var h = mixedKeyedHash;
        for (var i = 0; i < N; i++)
        {
            if (h.TryGetValue(mixedKeys[i], out var v))
            {
                sum += v.FixnumValue;
            }
        }
        return sum;
    }

    // ---- Iteration ----

    [Benchmark]
    public long IterateSymbolKeyed()
    {
        long sum = 0;
        foreach (var kv in symbolKeyedHash)
        {
            sum += kv.Value.FixnumValue;
        }
        return sum;
    }

    // ---- Keys span access (specialized hashes materialize MRubyValue[]) ----

    [Benchmark]
    public int KeysAccessSymbolKeyed()
    {
        var keys = symbolKeyedHash.Keys;
        return keys.Length;
    }

    [Benchmark]
    public int KeysAccessMixed()
    {
        var keys = mixedKeyedHash.Keys;
        return keys.Length;
    }
}
