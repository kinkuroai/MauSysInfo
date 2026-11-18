using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MauSysInfo.Services;

namespace MauSysInfo.ViewModels;

public sealed partial class BenchViewModel : ObservableObject
{
    private static readonly TimeSpan RunLength = TimeSpan.FromSeconds(5);
    private CancellationTokenSource? _cts;

    public int ThreadCount { get; } = Environment.ProcessorCount;

    public string Explanation =>
        $"Runs a fixed mix of floating-point and integer work for {RunLength.TotalSeconds:0} seconds on one thread, " +
        $"then on all {ThreadCount} threads. Higher is better. Scores only compare with other Mau SysInfo results, " +
        "so close other heavy programs first.";

    [ObservableProperty] private string _singleScore = "—";
    [ObservableProperty] private string _multiScore = "—";
    [ObservableProperty] private string _ratio = "—";
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _status = "Ready.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(RunCommand), nameof(StressCommand))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(StressButtonText))]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private bool _isStressing;

    public bool IsIdle => !IsRunning && !IsStressing;
    public string StressButtonText => IsStressing ? "Stop stress test" : "Stress test";

    private bool CanRun() => !IsRunning && !IsStressing;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        _cts = new CancellationTokenSource();
        IsRunning = true;
        SingleScore = MultiScore = Ratio = "…";
        try
        {
            var progress = StartProgress(RunLength * 2, _cts.Token);

            Status = "Single thread…";
            double single = await Task.Run(() => Benchmark.Run(1, RunLength, _cts.Token));
            SingleScore = Format(single);

            Status = $"All {ThreadCount} threads…";
            double multi = await Task.Run(() => Benchmark.Run(ThreadCount, RunLength, _cts.Token));
            MultiScore = Format(multi);
            Ratio = $"{(multi / single).ToString("0.00", CultureInfo.InvariantCulture)} ×";
            Status = $"Finished at {DateTime.Now:HH:mm}.";
            await progress;
        }
        catch (OperationCanceledException)
        {
            Status = "Cancelled.";
            if (SingleScore == "…") SingleScore = "—";
            if (MultiScore == "…") MultiScore = "—";
            if (Ratio == "…") Ratio = "—";
        }
        finally
        {
            Progress = 0;
            IsRunning = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    private bool CanStress() => !IsRunning;

    /// <summary>Starts or stops the stress test (a plain command, so the same button can stop it while it runs).</summary>
    [RelayCommand(CanExecute = nameof(CanStress))]
    private void Stress()
    {
        if (IsStressing) _cts?.Cancel();
        else _ = RunStressAsync();
    }

    private async Task RunStressAsync()
    {
        _cts = new CancellationTokenSource();
        IsStressing = true;
        var started = DateTime.Now;
        Status = $"Stress test running on all {ThreadCount} threads since {started:HH:mm:ss}. Watch clocks on the CPU page.";
        try
        {
            await Benchmark.StressAsync(ThreadCount, _cts.Token);
        }
        finally
        {
            Status = $"Stress test stopped after {Units.Duration(DateTime.Now - started)}.";
            IsStressing = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    /// <summary>Stops a run or stress test (window closing).</summary>
    public void StopAll() => _cts?.Cancel();

    private async Task StartProgress(TimeSpan total, CancellationToken token)
    {
        var start = DateTime.Now;
        try
        {
            while (!token.IsCancellationRequested && Progress < 1)
            {
                Progress = Math.Min(1, (DateTime.Now - start) / total);
                await Task.Delay(100, token);
            }
        }
        catch (OperationCanceledException) { }
    }

    private static string Format(double score) => score.ToString("N0", CultureInfo.CurrentCulture);
}
