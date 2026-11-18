using System.Windows;
using System.Windows.Controls;
using MauSysInfo.Models;
using MauSysInfo.Services;
using MauSysInfo.ViewModels;
using Microsoft.Win32;

namespace MauSysInfo;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel(Dispatcher);
        DataContext = _vm;

        var icon = AppInfo.LoadIcon();
        if (icon is null) AppIcon.Visibility = Visibility.Collapsed;
        else AppIcon.Source = icon;

        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedPage)) ContentScroll.ScrollToTop();
        };
        Loaded += async (_, _) =>
        {
            // The update check runs alongside the hardware scan; it never blocks the window.
            if (AppSettings.Current.CheckForUpdates) _ = _vm.Update.CheckAsync();
            await _vm.InitializeAsync();
        };
        Closed += (_, _) => _vm.Dispose();
    }

    private void OnAboutClick(object sender, RoutedEventArgs e) => AboutWindow.ShowFor(this);

    private void OnSaveReport(object sender, System.Windows.Input.ExecutedRoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save report",
            Filter = "Text file (*.txt)|*.txt",
            FileName = $"MauSysInfo-{Environment.MachineName}-{DateTime.Now:yyyy-MM-dd}.txt",
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllText(dialog.FileName, _vm.BuildReport());
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Couldn't save the report:\n{ex.Message}", AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnCopyReport(object sender, System.Windows.Input.ExecutedRoutedEventArgs e) => SetClipboard(_vm.BuildReport());

    private void OnCopyValue(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is InfoField field) SetClipboard(field.Value);
    }

    private void OnCopyLine(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is InfoField field) SetClipboard($"{field.Label}: {field.Value}");
    }

    private void SetClipboard(string text)
    {
        // The clipboard can be briefly locked by another program (clipboard managers, RDP).
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(text, true);
                return;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                Thread.Sleep(50);
            }
        }
        MessageBox.Show(this, "The clipboard is in use by another program. Try again.", AppInfo.Name,
            MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
