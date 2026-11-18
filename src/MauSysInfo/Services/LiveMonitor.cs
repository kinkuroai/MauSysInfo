using System.Diagnostics;
using System.Windows.Threading;
using MauSysInfo.Models;

namespace MauSysInfo.Services;

/// <summary>
/// Samples clocks, usage and memory once a second and pushes the values into the page fields.
/// Clocks come from the "% Processor Performance" counter times the base clock, which is how Task Manager
/// shows the speed; it needs no driver, so it works without administrator rights.
/// </summary>
public sealed class LiveMonitor : IDisposable
{
    private const string Category = "Processor Information";
    private readonly Dispatcher _dispatcher;
    private readonly CancellationTokenSource _cts = new();
    private readonly Dictionary<string, InfoField> _fields = [];
    private readonly List<(ThreadTile Tile, LogicalCpu Cpu)> _threads = [];
    private double _baseMhz;
    private Dictionary<string, (CounterSample Perf, CounterSample Utility, CounterSample Time)>? _previous;
    private Task? _loop;

    public LiveMonitor(Dispatcher dispatcher) => _dispatcher = dispatcher;

    /// <summary>Latest whole-CPU clock in MHz, for the benchmark page.</summary>
    public double CurrentMhz { get; private set; }

    public void Attach(PageData page)
    {
        lock (_fields)
        {
            foreach (var (key, field) in page.Live) _fields[key] = field;
            _threads.AddRange(page.Threads);
        }
    }

    /// <summary>Forgets the attached fields (before a refresh replaces the cards).</summary>
    public void Reset()
    {
        lock (_fields)
        {
            _fields.Clear();
            _threads.Clear();
        }
    }

    public void Start()
    {
        _baseMhz = ReadBaseMhz();
        _loop ??= Task.Run(() => RunAsync(_cts.Token));
    }

    private async Task RunAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        do
        {
            try { Tick(); }
            catch { /* counters can vanish briefly (e.g. after sleep); try again next second */ }
        }
        while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false));
    }

    private void Tick()
    {
        var updates = new List<Action>();
        InfoField? Field(string key) { lock (_fields) return _fields.GetValueOrDefault(key); }

        // CPU
        var current = ReadProcessorCounters();
        if (current is not null && _previous is not null)
        {
            double Perf(string instance) =>
                current.TryGetValue(instance, out var c) && _previous.TryGetValue(instance, out var p) ? SafeCalc(p.Perf, c.Perf) : double.NaN;
            double Utility(string instance) =>
                current.TryGetValue(instance, out var c) && _previous.TryGetValue(instance, out var p) ? SafeCalc(p.Utility, c.Utility) : double.NaN;

            double totalPerf = Perf("_Total");
            double totalUtil = Utility("_Total");
            if (!double.IsNaN(totalPerf) && _baseMhz > 0)
            {
                CurrentMhz = _baseMhz * totalPerf / 100;
                var clockText = Units.Clock(CurrentMhz);
                if (Field(LiveKeys.CpuClock) is { } f) updates.Add(() => f.Value = clockText);
            }
            if (!double.IsNaN(totalUtil) && Field(LiveKeys.CpuUsage) is { } u)
            {
                double frac = Math.Clamp(totalUtil / 100, 0, 1);
                updates.Add(() => { u.Value = Units.Percent(frac); u.Meter = frac; });
            }

            List<(ThreadTile Tile, LogicalCpu Cpu)> threads;
            lock (_fields) threads = [.. _threads];
            foreach (var (tile, cpu) in threads)
            {
                double perf = Perf(cpu.CounterInstance);
                double util = Utility(cpu.CounterInstance);
                string clock = !double.IsNaN(perf) && _baseMhz > 0 ? Units.Clock(_baseMhz * perf / 100) : "—";
                double frac = double.IsNaN(util) ? 0 : Math.Clamp(util / 100, 0, 1);
                string usageText = double.IsNaN(util) ? "—" : Units.Percent(frac);
                updates.Add(() => { tile.Clock = clock; tile.Usage = frac; tile.UsageText = usageText; });
            }
        }
        if (current is not null) _previous = current;

        // Memory
        if (Native.MemoryStatus() is { } m)
        {
            double total = m.ullTotalPhys, avail = m.ullAvailPhys;
            double used = total - avail;
            double commitLimit = m.ullTotalPageFile, commitUsed = m.ullTotalPageFile - m.ullAvailPageFile;
            if (Field(LiveKeys.MemoryInUse) is { } inUse)
            {
                var text = $"{Units.Bytes(used)} of {Units.Bytes(total)} ({Units.Percent(used / total)})";
                updates.Add(() => { inUse.Value = text; inUse.Meter = used / total; });
            }
            if (Field(LiveKeys.MemoryAvailable) is { } av)
            {
                var text = Units.Bytes(avail);
                updates.Add(() => av.Value = text);
            }
            if (Field(LiveKeys.MemoryCommitted) is { } cm)
            {
                var text = $"{Units.Bytes(commitUsed)} of {Units.Bytes(commitLimit)}";
                updates.Add(() => cm.Value = text);
            }
        }

        // Uptime
        if (Field(LiveKeys.Uptime) is { } up)
        {
            var text = Units.Duration(TimeSpan.FromMilliseconds(Environment.TickCount64));
            updates.Add(() => up.Value = text);
        }

        if (updates.Count > 0)
            _dispatcher.BeginInvoke(() => { foreach (var a in updates) a(); }, DispatcherPriority.Background);
    }

    private static double SafeCalc(CounterSample previous, CounterSample current)
    {
        try { return CounterSample.Calculate(previous, current); }
        catch { return double.NaN; }
    }

    /// <summary>All instances of three counters in one read (cheaper than one PerformanceCounter per thread).</summary>
    private static Dictionary<string, (CounterSample Perf, CounterSample Utility, CounterSample Time)>? ReadProcessorCounters()
    {
        try
        {
            var data = new PerformanceCounterCategory(Category).ReadCategory();
            var perf = data["% Processor Performance"];
            var util = data["% Processor Utility"] ?? data["% Processor Time"];
            var time = data["% Processor Time"];
            if (perf is null || util is null || time is null) return null;

            var result = new Dictionary<string, (CounterSample, CounterSample, CounterSample)>();
            foreach (InstanceData p in perf.Values)
            {
                var name = p.InstanceName;
                var u = util[name];
                var t = time[name];
                if (u is null || t is null) continue;
                // "_Total" is spelled that way; per-NUMA totals like "0,_Total" are skipped by name lookups.
                result[name] = (p.Sample, u.Sample, t.Sample);
            }
            return result;
        }
        catch
        {
            return null;
        }
    }

    private static double ReadBaseMhz()
    {
        var wmi = Wmi.First("SELECT MaxClockSpeed FROM Win32_Processor");
        if (wmi?.Long("MaxClockSpeed") is { } mhz and > 0) return mhz;
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return key?.GetValue("~MHz") is int r ? r : 0;
        }
        catch { return 0; }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
