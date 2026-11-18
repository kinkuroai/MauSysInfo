using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MauSysInfo.Services;

/// <summary>Name, version and icon of this app, read from the build so they never drift.</summary>
public static class AppInfo
{
    public const string Name = "Mau SysInfo";
    public const string Author = "Mausy";

    /// <summary>The only official download location.</summary>
    public const string OfficialSite = "https://downloads.kinkuroshideout.com";

    /// <summary>The installer's AppId (installer\MauSysInfo.iss); its uninstall key is "{AppId}_is1".</summary>
    public const string InstallerAppId = "{6493A0E8-1709-4FFF-A69F-A6AB360BC0BE}";

    /// <summary>
    /// Where Setup installed this exe, or null for a portable copy or a development build (nothing to update
    /// in place). Setup records the folder in its uninstall key: HKLM for everyone, HKCU for one user.
    /// </summary>
    public static InstallScope? FindInstallation(string? exeDirectory = null)
    {
        exeDirectory ??= AppContext.BaseDirectory;
        if (!TryNormalizeFolder(exeDirectory, out var self)) return null;
        var subKey = $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{InstallerAppId}_is1";
        foreach (var (hive, scope) in new[]
                 {
                     (Microsoft.Win32.RegistryHive.LocalMachine, InstallScope.AllUsers),
                     (Microsoft.Win32.RegistryHive.CurrentUser, InstallScope.CurrentUser),
                 })
        {
            try
            {
                using var root = Microsoft.Win32.RegistryKey.OpenBaseKey(hive, Microsoft.Win32.RegistryView.Registry64);
                using var key = root.OpenSubKey(subKey);
                if (key?.GetValue("InstallLocation") is string location &&
                    TryNormalizeFolder(location, out var installed) &&
                    installed.Equals(self, StringComparison.OrdinalIgnoreCase))
                    return scope;
            }
            catch
            {
            }
        }
        return null;
    }

    public static bool IsInstalledCopy(string? exeDirectory = null) => FindInstallation(exeDirectory) is not null;

    /// <summary>Full path without a trailing backslash, so "C:\x\" and "C:\x" compare equal.</summary>
    internal static bool TryNormalizeFolder(string? path, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            var trimmed = path.Trim().Trim('"');
            if (!Path.IsPathFullyQualified(trimmed)) return false;
            normalized = Path.GetFullPath(trimmed).TrimEnd('\\');
            return normalized.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>"0.1.0" — the project's &lt;Version&gt;, without the "+commit" build suffix.</summary>
    public static string Version { get; } = ReadVersion();

    public static string Runtime { get; } = $"{RuntimeInformation.FrameworkDescription} · {RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}";

    private static string ReadVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            int plus = informational.IndexOf('+');
            return plus > 0 ? informational[..plus] : informational;
        }
        return assembly.GetName().Version?.ToString(3) ?? "unknown";
    }

    /// <summary>The largest frame of Assets\app.ico, or null while the project has no icon yet.</summary>
    public static ImageSource? LoadIcon()
    {
        try
        {
            using var stream = typeof(AppInfo).Assembly.GetManifestResourceStream("MauSysInfo.app.ico");
            if (stream is null) return null;
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.OrderByDescending(f => f.PixelWidth).First();
            frame.Freeze();
            return frame;
        }
        catch
        {
            return null; // no icon resource in this build
        }
    }
}
