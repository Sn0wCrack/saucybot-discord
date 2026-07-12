using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Running;

var config = ManualConfig.CreateEmpty()
    .AddLogger(NullLogger.Instance)
    .AddColumnProvider(DefaultConfig.Instance.GetColumnProviders().ToArray())
    .AddExporter(DefaultConfig.Instance.GetExporters().ToArray())
    .AddDiagnoser(DefaultConfig.Instance.GetDiagnosers().ToArray())
    .AddAnalyser(DefaultConfig.Instance.GetAnalysers().ToArray())
    .AddJob(DefaultConfig.Instance.GetJobs().ToArray())
    .AddValidator(DefaultConfig.Instance.GetValidators().ToArray())
    .WithUnionRule(ConfigUnionRule.AlwaysUseGlobal);

var summaries = BenchmarkRunner.Run(typeof(Program).Assembly, config);

foreach (var summary in summaries)
{
    MarkdownExporter.Console.ExportToLog(summary, ConsoleLogger.Default);
}

