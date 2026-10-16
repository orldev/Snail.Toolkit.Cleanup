using BenchmarkDotNet.Running;
using Snail.Toolkit.Cleanup.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(PlanBenchmarks).Assembly).Run(args);
