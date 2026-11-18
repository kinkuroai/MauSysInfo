using Microsoft.Win32;

namespace MauSysInfo.Services;

public static class MainboardCollector
{
    public static PageData Collect()
    {
        var page = new PageData();

        var board = Wmi.First("SELECT Manufacturer, Product, Version, SerialNumber FROM Win32_BaseBoard");
        var b = page.AddBlock("Motherboard");
        b.Add("Manufacturer", board?.Str("Manufacturer"));
        b.Add("Model", board?.Str("Product"));
        b.Add("Revision", board?.Str("Version"));
        b.Add("Serial number", board?.Str("SerialNumber"));

        var bios = Wmi.First("SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate, SMBIOSMajorVersion, SMBIOSMinorVersion FROM Win32_BIOS");
        var f = page.AddBlock("BIOS / UEFI");
        f.Add("Vendor", bios?.Str("Manufacturer"));
        f.Add("Version", bios?.Str("SMBIOSBIOSVersion"));
        f.Add("Date", bios?.Date("ReleaseDate")?.ToString("yyyy-MM-dd"));
        f.Add("Firmware mode", FirmwareMode());
        f.Add("Secure Boot", SecureBoot());
        if (bios?.Long("SMBIOSMajorVersion") is { } major and > 0)
            f.Add("SMBIOS version", $"{major}.{bios.Long("SMBIOSMinorVersion") ?? 0}");

        var cs = Wmi.First("SELECT Manufacturer, Model, SystemFamily, SystemSKUNumber FROM Win32_ComputerSystem");
        var enclosure = Wmi.First("SELECT ChassisTypes FROM Win32_SystemEnclosure");
        var s = page.AddBlock("Computer");
        s.Add("Manufacturer", cs?.Str("Manufacturer"));
        s.Add("Model", cs?.Str("Model"));
        s.Add("Family", cs?.Str("SystemFamily"));
        s.Add("SKU", cs?.Str("SystemSKUNumber"));
        if (enclosure?.Raw("ChassisTypes") is ushort[] { Length: > 0 } chassis)
            s.Add("Chassis", ChassisName(chassis[0]));

        return page.WithoutEmptyBlocks();
    }

    public static string? FirmwareMode()
    {
        try
        {
            if (!Native.GetFirmwareType(out int type)) return null;
            return type switch { 1 => "Legacy BIOS", 2 => "UEFI", _ => null };
        }
        catch { return null; }
    }

    public static string? SecureBoot()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
            return key?.GetValue("UEFISecureBootEnabled") switch
            {
                1 => "On",
                0 => "Off",
                _ => "Not supported",
            };
        }
        catch { return null; }
    }

    /// <summary>SMBIOS chassis type codes (DMTF DSP0134, table 17).</summary>
    public static string? ChassisName(int code) => code switch
    {
        3 => "Desktop",
        4 => "Low-profile desktop",
        5 => "Pizza box",
        6 => "Mini tower",
        7 => "Tower",
        8 => "Portable",
        9 => "Laptop",
        10 => "Notebook",
        11 => "Handheld",
        12 => "Docking station",
        13 => "All-in-one",
        14 => "Sub-notebook",
        15 => "Space-saving",
        16 => "Lunch box",
        17 => "Main server chassis",
        23 => "Rack mount",
        24 => "Sealed-case PC",
        30 => "Tablet",
        31 => "Convertible",
        32 => "Detachable",
        33 => "IoT gateway",
        34 => "Embedded PC",
        35 => "Mini PC",
        36 => "Stick PC",
        _ => null,
    };
}
