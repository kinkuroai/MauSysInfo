using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MauSysInfo.Services;

public static class SystemCollector
{
    public static PageData Collect()
    {
        var page = new PageData();

        // Windows
        var win = page.AddBlock("Windows");
        string? productName = null, edition = null, displayVersion = null, build = null;
        int ubr = 0;
        long installDate = 0;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            productName = key?.GetValue("ProductName") as string;
            edition = key?.GetValue("EditionID") as string;
            displayVersion = key?.GetValue("DisplayVersion") as string ?? key?.GetValue("ReleaseId") as string;
            build = key?.GetValue("CurrentBuildNumber") as string ?? key?.GetValue("CurrentBuild") as string;
            ubr = key?.GetValue("UBR") is int u ? u : 0;
            installDate = key?.GetValue("InstallDate") is int d ? (uint)d : 0;
        }
        catch { }

        int.TryParse(build, NumberStyles.Integer, CultureInfo.InvariantCulture, out int buildNumber);
        win.Add("Edition", WindowsName(productName, buildNumber));
        win.Add("Version", displayVersion);
        win.Add("OS build", build is null ? null : ubr > 0 ? $"{build}.{ubr}" : build);
        win.Add("Architecture", RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "64-bit (x64)",
            Architecture.Arm64 => "64-bit (ARM64)",
            Architecture.X86 => "32-bit (x86)",
            var a => a.ToString(),
        });
        if (installDate > 0)
            win.Add("Installed", DateTimeOffset.FromUnixTimeSeconds(installDate).LocalDateTime.ToString("yyyy-MM-dd"),
                "Date of the last Windows installation or feature upgrade");
        page.Live[LiveKeys.Uptime] = win.AddLive("Uptime", "Time since Windows last started (or woke from a Fast Startup shutdown)");
        page.Live[LiveKeys.Uptime].Value = Units.Duration(TimeSpan.FromMilliseconds(Environment.TickCount64));

        // This PC
        var cs = Wmi.First("SELECT Domain, PartOfDomain, Workgroup FROM Win32_ComputerSystem");
        var pc = page.AddBlock("This PC");
        pc.Add("Computer name", Environment.MachineName);
        pc.Add("User", Environment.UserName);
        if (cs?.Bool("PartOfDomain") == true) pc.Add("Domain", cs.Str("Domain"));
        else pc.Add("Workgroup", cs?.Str("Workgroup") ?? cs?.Str("Domain"));
        pc.Add("Language", CultureInfo.CurrentUICulture.DisplayName);
        pc.Add("Time zone", TimeZoneInfo.Local.DisplayName);

        // Security
        var sec = page.AddBlock("Security");
        sec.Add("Firmware mode", MainboardCollector.FirmwareMode());
        sec.Add("Secure Boot", MainboardCollector.SecureBoot());
        sec.Add("TPM", TpmVersion(), "Trusted Platform Module, required by Windows 11");
        var guard = Wmi.First("SELECT VirtualizationBasedSecurityStatus, SecurityServicesRunning FROM Win32_DeviceGuard", Wmi.DeviceGuard);
        if (guard is not null)
        {
            sec.Add("Virtualization-based security", (guard.Long("VirtualizationBasedSecurityStatus") ?? -1) switch
            {
                0 => "Off",
                1 => "Enabled, not running",
                2 => "Running",
                _ => null,
            });
            var running = guard.Raw("SecurityServicesRunning") as uint[] ?? [];
            sec.Add("Memory integrity (HVCI)", running.Contains(2u) ? "On" : "Off",
                "Core isolation → Memory integrity in Windows Security");
            if (running.Contains(1u)) sec.Add("Credential Guard", "On");
        }

        // Battery (laptops, tablets, UPS reported as battery)
        foreach (var battery in Wmi.Query("SELECT Name, EstimatedChargeRemaining, BatteryStatus, EstimatedRunTime FROM Win32_Battery"))
        {
            var bat = page.AddBlock("Battery", battery.Str("Name"));
            long charge = battery.Long("EstimatedChargeRemaining") ?? -1;
            if (charge >= 0) bat.Add("Charge", $"{charge} %", meter: Math.Clamp(charge / 100.0, 0, 1));
            bat.Add("Status", (battery.Long("BatteryStatus") ?? 0) switch
            {
                1 => "Discharging",
                2 => "Plugged in",
                3 => "Fully charged",
                4 => "Low",
                5 => "Critical",
                6 or 7 or 8 or 9 => "Charging",
                11 => "Partially charged",
                _ => null,
            });
            long minutes = battery.Long("EstimatedRunTime") ?? 0;
            if (minutes is > 0 and < 71582788) bat.Add("Time remaining", Units.Duration(TimeSpan.FromMinutes(minutes)));
        }

        // This app's runtime, handy in bug reports
        var app = page.AddBlock("Mau SysInfo");
        app.Add("Version", AppInfo.Version);
        app.Add("Runtime", AppInfo.Runtime);
        app.Add("Running as administrator", ShellActions.IsElevated ? "Yes" : "No");

        return page.WithoutEmptyBlocks();
    }

    /// <summary>
    /// Windows 11 still reports "Windows 10 ..." as ProductName; build 22000 and later is Windows 11.
    /// </summary>
    public static string? WindowsName(string? productName, int build)
    {
        if (string.IsNullOrWhiteSpace(productName)) return null;
        if (build >= 22000 && productName.StartsWith("Windows 10", StringComparison.OrdinalIgnoreCase))
            return "Windows 11" + productName["Windows 10".Length..];
        return productName;
    }

    private static string? TpmVersion()
    {
        try
        {
            uint result = Native.Tbsi_GetDeviceInfo((uint)Marshal.SizeOf<Native.TPM_DEVICE_INFO>(), out var info);
            if (result != 0) return "Not found";
            return info.tpmVersion switch { 1 => "TPM 1.2", 2 => "TPM 2.0", _ => "Present" };
        }
        catch
        {
            return null;
        }
    }
}
