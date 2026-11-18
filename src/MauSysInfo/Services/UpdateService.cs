using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MauSysInfo.Services;

/// <summary>update.json on the official site, written by build-installer.ps1.</summary>
public sealed class UpdateManifest
{
    [JsonPropertyName("version")] public string? Version { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("sha256")] public string? Sha256 { get; set; }
    [JsonPropertyName("notes")] public string? Notes { get; set; }
    [JsonPropertyName("published")] public string? Published { get; set; }
}

public sealed record UpdateInfo(Version Version, Uri Download, string Sha256, string? Notes)
{
    public string VersionText => UpdateService.Format(Version);
}

public enum UpdateCheckStatus { UpToDate, Available, Failed }

public sealed record UpdateCheckResult(UpdateCheckStatus Status, UpdateInfo? Update = null, string? Error = null);

/// <summary>
/// Checks the official site for a newer version, downloads its installer and verifies it before it runs.
/// The installer runs with administrator rights for a Program Files install, so a tampered download would be
/// dangerous: it must come over HTTPS from the official site, match the SHA-256 in update.json, and — once
/// releases are code-signed — be signed by the same publisher as the running copy.
/// </summary>
public sealed class UpdateService
{
    /// <summary>Where releases are published. build-installer.ps1 uses the same folder.</summary>
    public const string ReleaseFolder = AppInfo.OfficialSite + "/mau-sysinfo/";
    public const string ManifestUrl = ReleaseFolder + "update.json";

    private const long MaxManifestBytes = 64 * 1024;
    private const long MaxInstallerBytes = 1024L * 1024 * 1024;

    private readonly HttpClient _http;
    private readonly Uri _manifestUrl;
    private readonly string _allowedHost;

    /// <param name="http">Tests pass a client with a fake handler; null uses a real one.</param>
    public UpdateService(HttpClient? http = null, string manifestUrl = ManifestUrl)
    {
        _manifestUrl = new Uri(manifestUrl);
        _allowedHost = _manifestUrl.Host;
        _http = http ?? CreateClient();
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        // Identifies the app and its version only.
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"MauSysInfo/{AppInfo.Version}");
        return client;
    }

    public async Task<UpdateCheckResult> CheckAsync(string currentVersion, CancellationToken ct = default)
    {
        string json;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            using var response = await _http.GetAsync(_manifestUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return new(UpdateCheckStatus.Failed, Error: $"No update information has been published on {_allowedHost} yet.");
            if (!response.IsSuccessStatusCode)
                return new(UpdateCheckStatus.Failed, Error: $"{_allowedHost} answered {(int)response.StatusCode} ({response.ReasonPhrase}).");
            if (response.Content.Headers.ContentLength > MaxManifestBytes)
                return new(UpdateCheckStatus.Failed, Error: "The update information is unexpectedly large.");
            json = await response.Content.ReadAsStringAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new(UpdateCheckStatus.Failed, Error: $"{_allowedHost} didn't answer in time.");
        }
        catch (HttpRequestException ex)
        {
            return new(UpdateCheckStatus.Failed, Error: $"Couldn't reach {_allowedHost}: {ex.Message}");
        }

        UpdateManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<UpdateManifest>(json);
        }
        catch (JsonException)
        {
            return new(UpdateCheckStatus.Failed, Error: "The update information on the site is not valid.");
        }

        var (update, error) = Validate(manifest);
        if (update is null) return new(UpdateCheckStatus.Failed, Error: error);
        if (!TryParseVersion(currentVersion, out var current))
            return new(UpdateCheckStatus.Failed, Error: $"This copy's version ({currentVersion}) can't be compared.");

        return update.Version > current
            ? new(UpdateCheckStatus.Available, update)
            : new(UpdateCheckStatus.UpToDate, update);
    }

    /// <summary>Accepts only a version, an HTTPS installer on the official site, and its SHA-256.</summary>
    public (UpdateInfo? Update, string? Error) Validate(UpdateManifest? manifest)
    {
        if (manifest is null || !TryParseVersion(manifest.Version, out var version))
            return (null, "The update information has no valid version number.");
        if (!Uri.TryCreate(manifest.Url, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps ||
            !url.Host.Equals(_allowedHost, StringComparison.OrdinalIgnoreCase) ||
            !url.AbsolutePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return (null, $"The update's download link isn't an installer on https://{_allowedHost}.");
        var hash = manifest.Sha256?.Trim().ToLowerInvariant();
        if (hash is not { Length: 64 } || !hash.All(Uri.IsHexDigit))
            return (null, "The update information has no valid SHA-256 checksum.");
        return (new UpdateInfo(version, url, hash, string.IsNullOrWhiteSpace(manifest.Notes) ? null : manifest.Notes.Trim()), null);
    }

    /// <summary>"1.2" / "1.2.3" / "1.2.3.4"; anything after a '-' or '+' (pre-release, build info) is ignored.</summary>
    public static bool TryParseVersion(string? text, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(text)) return false;
        var core = text.Trim().TrimStart('v', 'V').Split('-', '+')[0];
        if (!Version.TryParse(core, out var parsed)) return false;
        // Treat missing parts as 0 so 1.2 == 1.2.0.
        version = new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0), Math.Max(parsed.Revision, 0));
        return true;
    }

    public static string Format(Version v) =>
        v.Revision > 0 ? v.ToString(4) : v.ToString(3);

    public static string DownloadFolder => Path.Combine(Path.GetTempPath(), "MauSysInfo-Update");

    /// <summary>Downloads the installer and checks its SHA-256. Returns its path; deletes it and throws on any mismatch.</summary>
    public async Task<string> DownloadAsync(UpdateInfo update, IProgress<double>? progress = null, CancellationToken ct = default,
        string? folder = null)
    {
        folder ??= DownloadFolder;
        Directory.CreateDirectory(folder);
        var name = Path.GetFileName(update.Download.AbsolutePath);
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Length == 0) name = "MauSysInfo-Setup.exe";
        var path = Path.Combine(folder, name);

        try
        {
            using var response = await _http.GetAsync(update.Download, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            long? total = response.Content.Headers.ContentLength;
            if (total > MaxInstallerBytes) throw new InvalidDataException("The update is unexpectedly large.");

            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var source = await response.Content.ReadAsStreamAsync(ct))
            await using (var target = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), ct);
                    sha.AppendData(buffer, 0, read);
                    done += read;
                    if (done > MaxInstallerBytes) throw new InvalidDataException("The update is unexpectedly large.");
                    if (total > 0) progress?.Report((double)done / total.Value);
                }
            }

            var actual = Convert.ToHexStringLower(sha.GetHashAndReset());
            if (actual != update.Sha256)
                throw new InvalidDataException(
                    "The downloaded update doesn't match the checksum published with it, so it was not run. " +
                    "It may have been corrupted or tampered with.");
            return path;
        }
        catch
        {
            TryDelete(path);
            throw;
        }
    }

    /// <summary>
    /// Once releases are code-signed, an update must be signed by the same publisher as the running copy.
    /// Unsigned builds rely on the checksum alone. Returns null when the installer may run.
    /// </summary>
    public static string? CheckPublisher(string installerPath, string runningExePath)
    {
        var running = Authenticode.SignerOf(runningExePath);
        if (running is null) return null;
        var installer = Authenticode.SignerOf(installerPath);
        if (installer is null)
            return "The update is not signed, but this copy of Mau SysInfo is. It was not run.";
        if (!installer.Equals(running, StringComparison.Ordinal))
            return $"The update is signed by \"{installer}\" instead of \"{running}\". It was not run.";
        return null;
    }

    /// <summary>
    /// Arguments for a hands-off upgrade: progress only, no questions, no cancel, no restart, close
    /// whatever still uses the files, and start the app again when done (RELAUNCH, see the .iss).
    /// </summary>
    public const string SilentInstallArguments = "/SILENT /SP- /NOCANCEL /NORESTART /CLOSEAPPLICATIONS /RELAUNCH=1";

    /// <summary>
    /// Setup's install-mode switch, so the update goes where the current copy is: Program Files for everyone
    /// (/ALLUSERS, asks for administrator) or this user's own folder (/CURRENTUSER, no prompt).
    /// </summary>
    public static string ScopeArgument(InstallScope scope) => scope == InstallScope.CurrentUser ? "/CURRENTUSER" : "/ALLUSERS";

    /// <summary>Starts the installer without waiting (the app exits so its files can be replaced). Null when started.</summary>
    public static string? StartInstaller(string installerPath, InstallScope scope)
    {
        try
        {
            var arguments = $"{SilentInstallArguments} {ScopeArgument(scope)}";
            using var process = Process.Start(new ProcessStartInfo(installerPath, arguments) { UseShellExecute = true });
            return null;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return "";   // UAC prompt declined
        }
        catch (Exception ex)
        {
            return "Could not start the update: " + ex.Message;
        }
    }

    public static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }
}
