using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using MauSysInfo.Services;
using MauSysInfo.ViewModels;
using Microsoft.Win32;

namespace MauSysInfo.Tests;

/// <summary>Answers requests from a dictionary instead of the network.</summary>
internal sealed class FakeSite : HttpMessageHandler
{
    public Dictionary<string, Func<HttpResponseMessage>> Routes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Requested { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var url = request.RequestUri!.ToString();
        Requested.Add(url);
        if (Routes.TryGetValue(url, out var respond)) return Task.FromResult(respond());
        if (url.Contains("offline")) throw new HttpRequestException("No such host is known.");
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    public static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    public static HttpResponseMessage Bytes(byte[] data) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(data) };
}

public class UpdateTests
{
    private const string Manifest = "https://downloads.example.test/mau-sysinfo/update.json";
    private const string Setup = "https://downloads.example.test/mau-sysinfo/MauSysInfo-0.2.0-Setup.exe";
    private static readonly byte[] SetupBytes = Encoding.ASCII.GetBytes("pretend installer " + new string('x', 200_000));
    private static string Sha(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    private static (UpdateService Service, FakeSite Site) Create(string? manifestJson = null)
    {
        var site = new FakeSite();
        if (manifestJson is not null) site.Routes[Manifest] = () => FakeSite.Json(manifestJson);
        site.Routes[Setup] = () => FakeSite.Bytes(SetupBytes);
        return (new UpdateService(new HttpClient(site), Manifest), site);
    }

    private static string ManifestJson(string version = "0.2.0", string url = Setup, string? sha = null, string notes = "GPU temperatures") =>
        $$"""{ "version": "{{version}}", "url": "{{url}}", "sha256": "{{sha ?? Sha(SetupBytes)}}", "published": "2026-11-17", "notes": "{{notes}}" }""";

    [Theory]
    [InlineData("0.1.0", "0.1.0.0")]
    [InlineData("v1.2", "1.2.0.0")]
    [InlineData("1.2.3-beta.1", "1.2.3.0")]
    [InlineData("1.2.3+abc123", "1.2.3.0")]
    [InlineData("2.0.0.7", "2.0.0.7")]
    public void Versions_are_parsed_leniently(string text, string expected)
    {
        Assert.True(UpdateService.TryParseVersion(text, out var v));
        Assert.Equal(Version.Parse(expected), v);
    }

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData(null)]
    public void Nonsense_versions_are_rejected(string? text) => Assert.False(UpdateService.TryParseVersion(text, out _));

    [Theory]
    [InlineData("http://downloads.example.test/mau-sysinfo/x.exe", "download link")]          // not HTTPS
    [InlineData("https://evil.example.com/mau-sysinfo/x.exe", "download link")]              // another site
    [InlineData("https://downloads.example.test.evil.com/x.exe", "download link")]               // look-alike host
    [InlineData("https://downloads.example.test/mau-sysinfo/x.zip", "download link")]        // not an installer
    public void Download_links_off_the_official_site_are_refused(string url, string error)
    {
        var (service, _) = Create();
        var (update, message) = service.Validate(new UpdateManifest { Version = "9.0.0", Url = url, Sha256 = Sha(SetupBytes) });
        Assert.Null(update);
        Assert.Contains(error, message);
    }

    [Fact]
    public void A_missing_or_malformed_checksum_is_refused()
    {
        var (service, _) = Create();
        Assert.Contains("SHA-256", service.Validate(new UpdateManifest { Version = "9.0", Url = Setup }).Error);
        Assert.Contains("SHA-256", service.Validate(new UpdateManifest { Version = "9.0", Url = Setup, Sha256 = "abc" }).Error);
        Assert.Contains("SHA-256", service.Validate(new UpdateManifest { Version = "9.0", Url = Setup, Sha256 = new string('z', 64) }).Error);
        Assert.NotNull(service.Validate(new UpdateManifest { Version = "9.0", Url = Setup, Sha256 = Sha(SetupBytes).ToUpperInvariant() }).Update);
    }

    [Fact]
    public async Task Newer_version_is_offered_with_its_notes()
    {
        var (service, _) = Create(ManifestJson());
        var result = await service.CheckAsync("0.1.0");
        Assert.Equal(UpdateCheckStatus.Available, result.Status);
        Assert.Equal("0.2.0", result.Update!.VersionText);
        Assert.Equal("GPU temperatures", result.Update.Notes);
    }

    [Theory]
    [InlineData("0.2.0")]
    [InlineData("0.2")]
    [InlineData("0.3.0")]
    public async Task Same_or_older_version_is_up_to_date(string current)
    {
        var (service, _) = Create(ManifestJson());
        Assert.Equal(UpdateCheckStatus.UpToDate, (await service.CheckAsync(current)).Status);
    }

    [Fact]
    public async Task Problems_are_reported_not_thrown()
    {
        var (notPublished, _) = Create();
        Assert.Contains("No update information", (await notPublished.CheckAsync("0.1.0")).Error);

        var (garbage, _) = Create("<html>not json</html>");
        Assert.Contains("not valid", (await garbage.CheckAsync("0.1.0")).Error);

        var offline = new UpdateService(new HttpClient(new FakeSite()), "https://offline.example.test/update.json");
        var result = await offline.CheckAsync("0.1.0");
        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Contains("Couldn't reach offline.example.test", result.Error);
    }

    [Fact]
    public async Task Download_is_checked_against_the_published_checksum()
    {
        var folder = Path.Combine(Path.GetTempPath(), "MauUpdateTest" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var (service, _) = Create();
            var good = service.Validate(new UpdateManifest { Version = "0.2.0", Url = Setup, Sha256 = Sha(SetupBytes) }).Update!;
            var progress = new List<double>();
            var path = await service.DownloadAsync(good, new SyncProgress(progress.Add), folder: folder);
            Assert.Equal(SetupBytes, File.ReadAllBytes(path));
            Assert.Equal("MauSysInfo-0.2.0-Setup.exe", Path.GetFileName(path));
            Assert.Equal(1.0, progress.Last());

            // A different file than the one announced: refused and deleted.
            var tampered = good with { Sha256 = Sha(Encoding.ASCII.GetBytes("something else")) };
            var ex = await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(tampered, folder: folder));
            Assert.Contains("doesn't match the checksum", ex.Message);
            Assert.False(File.Exists(path));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }

    [Fact]
    public void Silent_update_restarts_the_app_without_questions()
    {
        foreach (var arg in new[] { "/SILENT", "/SP-", "/NORESTART", "/CLOSEAPPLICATIONS", "/RELAUNCH=1" })
            Assert.Contains(arg, UpdateService.SilentInstallArguments);
    }

    [Theory]
    [InlineData(InstallScope.AllUsers, "/ALLUSERS")]
    [InlineData(InstallScope.CurrentUser, "/CURRENTUSER")]
    public void Update_installs_where_the_current_copy_is(InstallScope scope, string argument) =>
        Assert.Equal(argument, UpdateService.ScopeArgument(scope));

    [Fact]
    public void Release_folder_is_on_the_official_site() =>
        Assert.StartsWith(AppInfo.OfficialSite + "/", UpdateService.ManifestUrl);

    [Fact]
    public async Task Status_line_shows_what_to_do()
    {
        var (service, _) = Create(ManifestJson());
        var installed = new UpdateViewModel(() => true, service) { Installation = InstallScope.AllUsers };
        Assert.Equal("Check for updates", installed.ActionText);

        await installed.CheckAsync();
        Assert.Equal(UpdateState.Available, installed.State);
        Assert.Equal("Version 0.2.0 is available", installed.Text);
        Assert.Equal("Update now", installed.ActionText);
        Assert.Contains("GPU temperatures", installed.ToolTip);

        var portable = new UpdateViewModel(() => true, service) { Installation = null };
        await portable.CheckAsync();
        Assert.Equal("Download", portable.ActionText);

        var (unpublished, _) = Create();
        var failed = new UpdateViewModel(() => true, unpublished);
        await failed.CheckAsync();
        Assert.Equal("Couldn't check for updates", failed.Text);
        Assert.Equal("Try again", failed.ActionText);
        Assert.Contains("No update information", failed.ToolTip);
    }
}

public class AuthenticodeTests
{
    private static string SignedExe => Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "..", "..", "..", "dotnet.exe");

    [Fact]
    public void Signed_and_unsigned_files_are_told_apart()
    {
        var signed = Path.GetFullPath(SignedExe);
        Assert.True(File.Exists(signed), signed);
        Assert.Contains("Microsoft", Authenticode.SignerOf(signed));

        var unsigned = Path.Combine(Path.GetTempPath(), $"MauUnsigned{Guid.NewGuid():N}.exe");
        File.WriteAllBytes(unsigned, [0x4D, 0x5A, 0, 0]);
        try
        {
            Assert.Null(Authenticode.SignerOf(unsigned));
            // Unsigned app: the checksum is the only check. Signed app: the update must be signed the same way.
            Assert.Null(UpdateService.CheckPublisher(unsigned, unsigned));
            Assert.Contains("not signed", UpdateService.CheckPublisher(unsigned, signed));
            Assert.Null(UpdateService.CheckPublisher(signed, signed));
        }
        finally
        {
            File.Delete(unsigned);
        }
    }
}

public class InstalledCopyTests
{
    [Fact]
    public void Only_the_folder_the_installer_registered_counts_as_installed()
    {
        var subKey = $@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{AppInfo.InstallerAppId}_is1";
        if (Registry.CurrentUser.OpenSubKey(subKey) is { } existing)
        {
            existing.Dispose();
            return; // a real per-user installation: leave it alone
        }
        var folder = Path.Combine(Path.GetTempPath(), "MauInstalledTest" + Guid.NewGuid().ToString("N")[..8]) + "\\";
        Directory.CreateDirectory(folder);
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(subKey)) key.SetValue("InstallLocation", folder);
            Assert.True(AppInfo.IsInstalledCopy(folder));
            Assert.True(AppInfo.IsInstalledCopy(folder.TrimEnd('\\')));
            Assert.False(AppInfo.IsInstalledCopy(Path.GetTempPath()));
            Assert.Equal(InstallScope.CurrentUser, AppInfo.FindInstallation(folder));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(subKey, false);
            Directory.Delete(folder);
        }
    }
}
