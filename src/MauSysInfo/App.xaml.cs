using System.Windows;
using System.Windows.Threading;

namespace MauSysInfo;

public partial class App : Application
{
    /// <summary>
    /// Held while the app runs so its installer and uninstaller (Inno Setup AppMutex) can ask to close it
    /// first. Must match AppMutex in installer\MauSysInfo.iss.
    /// </summary>
    public const string RunningMutexName = "MauSysInfoRunning";
    private readonly List<Mutex> _runningMutexes = [];

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnUnhandledException;

        HoldRunningMutex();

        // Troubleshooting switch for machines/sessions where GPU rendering shows a blank window.
        if (Environment.GetEnvironmentVariable("MAU_SOFTWARE_RENDER") == "1")
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

        base.OnStartup(e);
    }

    public void HoldRunningMutex()
    {
        if (_runningMutexes.Count > 0) return;
        // Plain name for this session, Global\ for other users' sessions.
        foreach (var name in new[] { RunningMutexName, @"Global\" + RunningMutexName })
        {
            try { _runningMutexes.Add(new Mutex(false, name)); }
            catch { }
        }
    }

    /// <summary>
    /// Before starting an update's installer: the app is about to exit, so Setup shouldn't stop to say it's
    /// still running.
    /// </summary>
    public void ReleaseRunningMutex()
    {
        foreach (var mutex in _runningMutexes) mutex.Dispose();
        _runningMutexes.Clear();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ReleaseRunningMutex();
        base.OnExit(e);
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.Message, "Mau SysInfo – unexpected error",
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
