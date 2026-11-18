using MauSysInfo.Models;
using MauSysInfo.Services;
using Xunit.Abstractions;

namespace MauSysInfo.Tests;

/// <summary>Runs every collector against this machine and prints the report, so a test run shows real output.</summary>
public class SmokeTests(ITestOutputHelper output)
{
    [Fact]
    public void All_collectors_run_and_report()
    {
        var pages = new (string, Func<PageData>)[]
        {
            ("CPU", CpuCollector.Collect),
            ("Mainboard", MainboardCollector.Collect),
            ("Memory", MemoryCollector.Collect),
            ("Graphics", GraphicsCollector.Collect),
            ("Storage", StorageCollector.Collect),
            ("Network", NetworkCollector.Collect),
            ("System", SystemCollector.Collect),
        };
        var results = pages.Select(p => (p.Item1, (IEnumerable<InfoBlock>)p.Item2().Blocks)).ToList();

        var cpu = results[0].Item2.OfType<FieldsBlock>().First();
        Assert.Contains(cpu.Fields, f => f.Label == "Name" && f.Value.Length > 0);

        var report = ReportWriter.Build(results, DateTime.Now);
        var path = Path.Combine(Path.GetTempPath(), "MauSysInfo-smoke-report.txt");
        File.WriteAllText(path, report);
        output.WriteLine(path);
        output.WriteLine(report);
    }

    [Fact]
    public void Benchmark_kernel_runs()
    {
        Assert.True(Benchmark.Run(1, TimeSpan.FromMilliseconds(200), CancellationToken.None) > 0);
    }
}
