using BenchmarkDotNet.Attributes;

namespace MRubyCS.Benchmark;

[Config(typeof(BenchmarkConfig))]
public class AoRenderBenchmark
{
    readonly RubyScriptLoader scriptLoader = new();

    [GlobalSetup]
    public void LoadScript()
    {
        scriptLoader.IncludeMathModule();
        scriptLoader.PreloadScriptFromFile("bm_ao_render.rb");
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