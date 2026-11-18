using System.Windows;
using MauSysInfo.Services;

namespace MauSysInfo;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        AppName.Text = AppInfo.Name;
        AppVersion.Text = $"Version {AppInfo.Version}";
        AppAuthor.Text = $"Created by {AppInfo.Author}";
        SiteLink.NavigateUri = new Uri(AppInfo.OfficialSite);
        SiteText.Text = new Uri(AppInfo.OfficialSite).Host;

        var icon = AppInfo.LoadIcon();
        if (icon is null) AppIcon.Visibility = Visibility.Collapsed;
        else AppIcon.Source = icon;

        Details.Text =
            $"{AppInfo.Runtime} · {(ShellActions.IsElevated ? "running as administrator" : "not running as administrator")}\n" +
            $"The only connection it makes is the update check, to {new Uri(AppInfo.OfficialSite).Host}, which sends just the app name and version. " +
            $"Settings: {Path.GetDirectoryName(AppSettings.DefaultPath)}";
    }

    public static void ShowFor(Window owner) => new AboutWindow { Owner = owner }.ShowDialog();

    private void OnSiteLinkNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        ShellActions.OpenUrl(e.Uri.AbsoluteUri);
        e.Handled = true;
    }
}
