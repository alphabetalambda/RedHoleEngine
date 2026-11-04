using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;

namespace RedHoleEngine.Benchmarks;

internal sealed class BenchConfig : ManualConfig
{
    public BenchConfig()
    {
        AddDiagnoser(MemoryDiagnoser.Default);
        AddJob(Job.ShortRun);
        AddColumn(StatisticColumn.Median);
        AddExporter(MarkdownExporter.GitHub);
        WithArtifactsPath(Path.Combine(Path.GetTempPath(), "rhe-bench"));
        WithOptions(ConfigOptions.DisableOptimizationsValidator);
    }
}
