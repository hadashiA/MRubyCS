using System.Reflection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using MRubyCS.Benchmark;

BenchmarkSwitcher.FromAssembly(Assembly.GetEntryAssembly()!).Run(args);

[Config(typeof(BenchmarkConfig))]
public class NumericOperationBenchmark() : MRubyBenchmarkBase("bm_numeric_op.rb");

[Config(typeof(BenchmarkConfig))]
public class FibBenchmark() : MRubyBenchmarkBase("bm_fib.rb");


// ---
// using var loader = new RubyScriptLoader();
//
// loader.PreloadScriptFromFile("bm_fib.rb");
//
// var result = loader.RunMRubyCS();
// Console.WriteLine(result);
//
// var result2 = loader.RunMRubyNative();
// Console.WriteLine(result2.IntValue);