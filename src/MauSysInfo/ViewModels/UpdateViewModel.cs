using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MauSysInfo.Services;

namespace MauSysInfo.ViewModels;

public enum UpdateState { NotChecked, Checking, UpToDate, Available, Downloading, Failed }

/// <summary>The update part of the status bar: checks the official site, downloads, verifies and installs.</summary>
public sealed partial class UpdateViewModel : ObservableObject
{
    private readonly UpdateService _service;
    private readonly Func<bool> _canInterrupt;

    /// <param name="canInterrupt">False while the benchmark or stress test runs (closing would discard it).</param>
    public UpdateViewModel(Func<bool> canInterrupt, UpdateService? service = null)
    {
        _canInterrupt = canInterrupt;
        _service = service ?? new UpdateService();
    }

    public string CurrentVersion { get; } = AppInfo.Version;

    /// <summary>
    /// Where Setup installed this copy (so an update can install over it), or null for a portable copy or a
    /// development build, which can only be pointed at the download page.
    /// </summary>
    public InstallScope? Installation { get; init; } = AppInfo.FindInstallation();

    public bool IsInstalledCopy => Installation is not null;

    /// <summary>The "check when Mau SysInfo starts" switch in the status line's right-click menu.</summary>
    public bool CheckOnStartup
    {
        get => AppSettings.Current.CheckForUpdates;
        set
        {
            if (AppSettings.Current.CheckForUpdates == value) return;
            AppSettings.Current.CheckForUpdates = value;
            AppSettings.Current.TrySave();
            OnPropertyChanged();
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Text), nameof(ToolTip), nameof(ActionText), nameof(HasAction), nameof(IsDownloading))]
    [NotifyCanExecuteChangedFor(nameof(RunActionCommand))]
    public partial UpdateState State { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Text))]
    public partial double Progress { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Text), nameof(ToolTip))]
    public partial UpdateInfo? Available { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToolTip))]
    public partial string? Error { get; set; }

    public bool IsDownloading => State == UpdateState.Downloading;

    public string Text => State switch
    {
        UpdateState.Checking => "Checking for updates…",
        UpdateState.UpToDate => $"Version {CurrentVersion} · up to date",
        UpdateState.Available => $"Version {Available?.VersionText} is available",
        UpdateState.Downloading => $"Downloading the update… {Progress:P0}",
        UpdateState.Failed => "Couldn't check for updates",
        _ => $"Version {CurrentVersion}",
    };

    public string? ToolTip => State switch
    {
        UpdateState.Available when Available?.Notes is { } notes => $"What's new:\n{notes}",
        UpdateState.Failed => Error,
        _ => null,
    };

    public string ActionText => State switch
    {
        UpdateState.Available => IsInstalledCopy ? "Update now" : "Download",
        UpdateState.Failed => "Try again",
        UpdateState.NotChecked => "Check for updates",
        _ => "",
    };

    public bool HasAction => ActionText.Length > 0;

    public async Task CheckAsync()
    {
        if (State is UpdateState.Checking or UpdateState.Downloading) return;
        State = UpdateState.Checking;
        var result = await _service.CheckAsync(CurrentVersion);
        Error = result.Error;
        Available = result.Status == UpdateCheckStatus.Available ? result.Update : null;
        State = result.Status switch
        {
            UpdateCheckStatus.Available => UpdateState.Available,
            UpdateCheckStatus.UpToDate => UpdateState.UpToDate,
            _ => UpdateState.Failed,
        };
    }

    /// <summary>From the right-click menu, whatever the current state (except mid-check or mid-download).</summary>
    [RelayCommand]
    private Task CheckNowAsync() => CheckAsync();

    [RelayCommand(CanExecute = nameof(CanRunAction))]
    private async Task RunActionAsync()
    {
        switch (State)
        {
            case UpdateState.Available when !IsInstalledCopy:
                // A portable copy or a development build: there's no installation to update in place.
                ShellActions.OpenUrl(AppInfo.OfficialSite);
                break;
            case UpdateState.Available:
                await InstallAsync();
                break;
            default:
                await CheckAsync();
                break;
        }
    }

    private bool CanRunAction() => HasAction;

    private async Task InstallAsync()
    {
        if (Available is not { } update) return;
        if (!_canInterrupt())
        {
            MessageBox.Show("Stop the benchmark or stress test first, then update.",
                "Update Mau SysInfo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var notes = update.Notes is null ? "" : $"What's new:\n{update.Notes}\n\n";
        var confirm = MessageBox.Show(
            $"Update Mau SysInfo from {CurrentVersion} to {update.VersionText}?\n\n{notes}" +
            $"The update is downloaded from {update.Download.Host} and checked before it runs. " +
            "Mau SysInfo then closes, installs it and starts again.",
            "Update Mau SysInfo", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes);
        if (confirm != MessageBoxResult.Yes) return;

        State = UpdateState.Downloading;
        Progress = 0;
        string installer;
        try
        {
            installer = await _service.DownloadAsync(update, new Progress<double>(p => Progress = p));
        }
        catch (Exception ex)
        {
            Fail("The update could not be downloaded:\n" + ex.Message);
            return;
        }

        if (UpdateService.CheckPublisher(installer, Environment.ProcessPath ?? "") is { } signatureError)
        {
            UpdateService.TryDelete(installer);
            Fail(signatureError);
            return;
        }

        // Let Setup see the app as closed (it waits on the app's mutex), then exit.
        var app = Application.Current as App;
        app?.ReleaseRunningMutex();
        var error = UpdateService.StartInstaller(installer, Installation ?? InstallScope.AllUsers);
        if (error is null)
        {
            Application.Current.Shutdown();
            return;
        }
        app?.HoldRunningMutex();
        State = UpdateState.Available;
        if (error.Length > 0) Fail(error);
    }

    private void Fail(string message)
    {
        State = UpdateState.Available;
        MessageBox.Show(message, "Update Mau SysInfo", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
