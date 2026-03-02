using BenchmarkDotNet.Attributes;

namespace MRubyCS.Benchmark;

[Config(typeof(BenchmarkConfig))]
public abstract class MRubyBenchmarkBase(string filename)
{
    readonly RubyScriptLoader scriptLoader = new();

    [GlobalSetup]
    public void LoadScript()
    {
        scriptLoader.PreloadScriptFromFile(filename);
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
