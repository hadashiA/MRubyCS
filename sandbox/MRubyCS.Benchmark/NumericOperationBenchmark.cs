using BenchmarkDotNet.Attributes;

namespace MRubyCS.Benchmark;

[Config(typeof(BenchmarkConfig))]
public class NumericOperationBenchmark
{
    readonly RubyScriptLoader scriptLoader = new();

    [GlobalSetup]
    public void LoadScript()
    {
        scriptLoader.PreloadScriptFromFile("bm_numeric_op.rb");
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        scriptLoader.Dispose();
    }

    [Benchmark]
    public void MRubyCS()
    {
        scriptLoader.RunMRubyCS();
    }

    [Benchmark]
    public unsafe void MRubyNative()
    {
        scriptLoader.RunMRubyNative();
    }
}