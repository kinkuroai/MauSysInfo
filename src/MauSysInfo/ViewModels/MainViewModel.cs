using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MauSysInfo.Models;
using MauSysInfo.Services;

namespace MauSysInfo.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly LiveMonitor _monitor;

    public MainViewModel(Dispatcher dispatcher)
    {
        _monitor = new LiveMonitor(dispatcher);
        Bench = new BenchViewModel();

        var benchPage = new PageViewModel("Benchmark", "", null);
        benchPage.SetBlocks([new BenchBlock(Bench) { Wide = true }]);

        Pages =
        [
            new PageViewModel("CPU", "", CpuCollector.Collect),
            new PageViewModel("Mainboard", "", MainboardCollector.Collect),
            new PageViewModel("Memory", "", MemoryCollector.Collect),
            new PageViewModel("Graphics", "", GraphicsCollector.Collect),
            new PageViewModel("Storage", "", StorageCollector.Collect),
            new PageViewModel("Network", "", NetworkCollector.Collect),
            new PageViewModel("System", "", SystemCollector.Collect),
            benchPage,
        ];
        _selectedPage = Pages[0];
        Update = new UpdateViewModel(() => Bench.IsIdle);
    }

    /// <summary>The update check shown on the right of the status line.</summary>
    public UpdateViewModel Update { get; }

    /// <summary>Left side of the status line.</summary>
    [ObservableProperty] private string _statusText = "Reading your hardware…";

    public IReadOnlyList<PageViewModel> Pages { get; }
    public BenchViewModel Bench { get; }

    [ObservableProperty] private PageViewModel _selectedPage;
    [ObservableProperty] private bool _isRefreshing;

    public bool IsElevated => ShellActions.IsElevated;
    public bool IsNotElevated => !IsElevated;
    public string WindowTitle => IsElevated ? $"{AppInfo.Name} (administrator)" : AppInfo.Name;

    /// <summary>Loads every page in parallel, then starts the once-a-second updates.</summary>
    public async Task InitializeAsync()
    {
        await LoadAllAsync();
        _monitor.Start();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsRefreshing) return;
        // Live fields belong to the old cards, so the monitor is rebuilt with the new ones.
        await LoadAllAsync();
    }

    private async Task LoadAllAsync()
    {
        IsRefreshing = true;
        StatusText = "Reading your hardware…";
        try
        {
            var results = await Task.WhenAll(Pages.Select(p => p.LoadAsync()));
            _monitor.Reset();
            foreach (var data in results)
                if (data is not null) _monitor.Attach(data);
        }
        finally
        {
            IsRefreshing = false;
            int failed = Pages.Count(p => p.Error is not null);
            StatusText = $"Read at {DateTime.Now:HH:mm} · clocks, usage and memory update every second" +
                         (failed > 0 ? $" · {failed} page(s) couldn't be read" : "") +
                         (IsElevated ? "" : " · drive temperature and wear need administrator");
        }
    }

    public string BuildReport() =>
        ReportWriter.Build(Pages.Select(p => (p.Title, p.AllBlocks)), DateTime.Now);

    [RelayCommand]
    private void RestartAsAdmin()
    {
        if (ShellActions.RestartElevated())
            Application.Current.Shutdown();
    }

    public void Dispose()
    {
        Bench.StopAll();
        _monitor.Dispose();
    }
}
