using System.Text.Json;

namespace MauSysInfo.Services;

public enum InstallScope { AllUsers, CurrentUser }

/// <summary>User preferences, stored as JSON in %LocalAppData%\MauSysInfo\settings.json.</summary>
public sealed class AppSettings
{
    /// <summary>Read update.json from the official site when the app starts.</summary>
    public bool CheckForUpdates { get; set; } = true;

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MauSysInfo", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static AppSettings? _current;

    /// <summary>The settings in effect for this run (loaded on first use).</summary>
    public static AppSettings Current
    {
        get => _current ??= Load(DefaultPath);
        set => _current = value;
    }

    public static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new AppSettings();
        }
        catch
        {
            // Corrupt or unreadable file: fall back to defaults rather than failing to start.
        }
        return new AppSettings();
    }

    /// <summary>Saves; a read-only profile just means the choice lasts for this run only.</summary>
    public bool TrySave(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
            return true;
        }
        catch
        {
            return false;
        }
    }
}
