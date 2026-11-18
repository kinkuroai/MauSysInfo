using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace MauSysInfo.Services;

public static partial class GraphicsCollector
{
    private const string DisplayClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    public static PageData Collect()
    {
        var page = new PageData();
        var vram = ReadVideoMemory();
        var modes = ReadDisplayModes();

        foreach (var gpu in Wmi.Query("SELECT Name, AdapterCompatibility, VideoProcessor, AdapterRAM, DriverVersion, DriverDate, " +
                                      "PNPDeviceID " +
                                      "FROM Win32_VideoController"))
        {
            string name = gpu.Str("Name") ?? "Display adapter";
            string? pnp = gpu.Str("PNPDeviceID");
            var card = page.AddBlock(name, gpu.Str("AdapterCompatibility"));
            card.Add("GPU", gpu.Str("VideoProcessor") is { } vp && vp != name ? vp : null);

            long memory = vram.FirstOrDefault(v => v.Matches(name, pnp)).Bytes;
            if (memory <= 0) memory = gpu.Long("AdapterRAM") ?? 0; // capped at 4 GB by WMI, so only a fallback
            card.Add("Dedicated memory", memory > 0 ? Units.Bytes(memory) : null,
                memory > 0 ? null : "Integrated graphics share system memory");

            string? driver = gpu.Str("DriverVersion");
            card.Add("Driver version", driver is null ? null :
                NvidiaDriverVersion(name, driver) is { } nv ? $"{nv} ({driver})" : driver);
            card.Add("Driver date", gpu.Date("DriverDate")?.ToString("yyyy-MM-dd"));
            card.Add("Device ID", PciIds(pnp), "PCI vendor:device, then the board maker's subsystem ID");


            var outputs = modes.Where(m => m.Adapter.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (outputs.Count > 0) card.Add("Displays attached", outputs.Count.ToString());
        }

        // Displays (desktop modes) and the monitors behind them
        var monitors = ReadMonitors();
        if (modes.Count > 0 || monitors.Count > 0)
        {
            var displays = page.AddBlock("Displays", wide: true);
            foreach (var mode in modes)
            {
                displays.Add(mode.Device.Replace(@"\\.\", "") + (mode.Primary ? " (main)" : ""),
                    $"{mode.Width} × {mode.Height} @ {mode.Hz} Hz, {mode.Bits}-bit colour — {mode.Adapter}");
            }
            foreach (var mon in monitors)
            {
                displays.Add("Monitor", mon);
            }
        }

        return page.WithoutEmptyBlocks();
    }

    private readonly record struct VideoMemory(string? Description, string? MatchingId, long Bytes)
    {
        public bool Matches(string name, string? pnp) =>
            (MatchingId is not null && pnp is not null && pnp.StartsWith(MatchingId, StringComparison.OrdinalIgnoreCase))
            || (Description is not null && Description.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The driver's own memory size from the display class key. Win32_VideoController.AdapterRAM is a 32-bit
    /// value and tops out at 4 GB, which is wrong for any modern card.
    /// </summary>
    private static List<VideoMemory> ReadVideoMemory()
    {
        var list = new List<VideoMemory>();
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(DisplayClassKey);
            if (root is null) return list;
            foreach (var sub in root.GetSubKeyNames().Where(n => n.All(char.IsDigit)))
            {
                try
                {
                    using var key = root.OpenSubKey(sub);
                    if (key is null) continue;
                    long bytes = key.GetValue("HardwareInformation.qwMemorySize") switch
                    {
                        long q => q,
                        byte[] { Length: >= 8 } b8 => BitConverter.ToInt64(b8),
                        _ => key.GetValue("HardwareInformation.MemorySize") switch
                        {
                            int d => (uint)d,
                            byte[] { Length: >= 4 } b4 => BitConverter.ToUInt32(b4),
                            _ => 0,
                        },
                    };
                    list.Add(new VideoMemory(key.GetValue("DriverDesc") as string, key.GetValue("MatchingDeviceId") as string, bytes));
                }
                catch { /* "Properties" subkey and others are admin-only */ }
            }
        }
        catch { }
        return list;
    }

    private sealed record DisplayMode(string Device, string Adapter, int Width, int Height, int Hz, int Bits, bool Primary);

    private static List<DisplayMode> ReadDisplayModes()
    {
        var list = new List<DisplayMode>();
        try
        {
            var dev = new Native.DISPLAY_DEVICE { cb = System.Runtime.InteropServices.Marshal.SizeOf<Native.DISPLAY_DEVICE>() };
            for (uint i = 0; Native.EnumDisplayDevices(null, i, ref dev, 0); i++)
            {
                if ((dev.StateFlags & Native.DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) != 0)
                {
                    var mode = new Native.DEVMODE { dmSize = (short)System.Runtime.InteropServices.Marshal.SizeOf<Native.DEVMODE>() };
                    if (Native.EnumDisplaySettings(dev.DeviceName, Native.ENUM_CURRENT_SETTINGS, ref mode))
                        list.Add(new DisplayMode(dev.DeviceName, dev.DeviceString, mode.dmPelsWidth, mode.dmPelsHeight,
                            mode.dmDisplayFrequency, mode.dmBitsPerPel, (dev.StateFlags & Native.DISPLAY_DEVICE_PRIMARY_DEVICE) != 0));
                }
                dev.cb = System.Runtime.InteropServices.Marshal.SizeOf<Native.DISPLAY_DEVICE>();
            }
        }
        catch { }
        return list;
    }

    /// <summary>"MSI MAG 275CQ E18, 27 in, made 2023" from the monitor's EDID (via WMI).</summary>
    private static List<string> ReadMonitors()
    {
        var sizes = new Dictionary<string, (long W, long H)>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Wmi.Query("SELECT InstanceName, MaxHorizontalImageSize, MaxVerticalImageSize FROM WmiMonitorBasicDisplayParams", Wmi.RootWmi))
            if (p.Str("InstanceName") is { } inst)
                sizes[inst] = (p.Long("MaxHorizontalImageSize") ?? 0, p.Long("MaxVerticalImageSize") ?? 0);

        var list = new List<string>();
        foreach (var m in Wmi.Query("SELECT InstanceName, UserFriendlyName, ManufacturerName, YearOfManufacture FROM WmiMonitorID", Wmi.RootWmi))
        {
            string? name = EdidString(m.Raw("UserFriendlyName"));
            string? maker = PnpVendor(EdidString(m.Raw("ManufacturerName")));
            var parts = new List<string>();
            parts.Add(string.Join(' ', new[] { maker, name ?? "Unnamed monitor" }.Where(s => s is not null)));
            if (m.Str("InstanceName") is { } inst && sizes.TryGetValue(inst, out var size) && size.W > 0 && size.H > 0)
            {
                double inches = Math.Sqrt(size.W * size.W + size.H * size.H) / 2.54;
                parts.Add($"{inches.ToString("0", CultureInfo.InvariantCulture)}\" ({size.W} × {size.H} cm)");
            }
            if (m.Long("YearOfManufacture") is { } year and > 1990) parts.Add($"made {year}");
            list.Add(string.Join(", ", parts));
        }
        return list;
    }

    private static string? EdidString(object? raw) => raw is ushort[] chars
        ? Wmi.Clean(new string(chars.TakeWhile(c => c != 0).Select(c => (char)c).ToArray()))
        : null;

    /// <summary>Three-letter PnP vendor codes from EDID for the common monitor brands.</summary>
    private static string? PnpVendor(string? code) => code switch
    {
        null => null,
        "MSI" => "MSI",
        "DEL" => "Dell",
        "SAM" or "SEC" => "Samsung",
        "GSM" => "LG",
        "ACI" or "AUS" => "ASUS",
        "ACR" => "Acer",
        "AOC" => "AOC",
        "BNQ" => "BenQ",
        "HWP" or "HPN" => "HP",
        "LEN" => "Lenovo",
        "PHL" => "Philips",
        "VSC" => "ViewSonic",
        "GBT" => "Gigabyte",
        "AUO" => "AU Optronics",
        "BOE" => "BOE",
        "CMN" => "Chimei Innolux",
        "SHP" => "Sharp",
        "LGD" => "LG Display",
        "APP" => "Apple",
        "SNY" => "Sony",
        "IVM" => "iiyama",
        "EIZ" or "ENC" => "EIZO",
        "XMI" => "Xiaomi",
        _ => code,
    };

    /// <summary>"10DE:2216, subsystem 1458:403E, rev A1" from "PCI\VEN_10DE&amp;DEV_2216&amp;SUBSYS_403E1458&amp;REV_A1\...".</summary>
    public static string? PciIds(string? pnp)
    {
        if (pnp is null) return null;
        var m = PciRegex().Match(pnp);
        if (!m.Success) return null;
        var text = $"{m.Groups["ven"].Value}:{m.Groups["dev"].Value}".ToUpperInvariant();
        if (m.Groups["sub"].Success)
        {
            var sub = m.Groups["sub"].Value.ToUpperInvariant();
            text += $", subsystem {sub[4..]}:{sub[..4]}";
        }
        if (m.Groups["rev"].Success) text += $", rev {m.Groups["rev"].Value.ToUpperInvariant()}";
        return text;
    }

    [GeneratedRegex(@"VEN_(?<ven>[0-9A-F]{4})&DEV_(?<dev>[0-9A-F]{4})(&SUBSYS_(?<sub>[0-9A-F]{8}))?(&REV_(?<rev>[0-9A-F]{2}))?", RegexOptions.IgnoreCase)]
    private static partial Regex PciRegex();

    /// <summary>
    /// NVIDIA's own version number ("617.42") from the Windows driver version ("32.0.16.1742"):
    /// the last five digits of the last two parts.
    /// </summary>
    public static string? NvidiaDriverVersion(string adapterName, string windowsVersion)
    {
        if (!adapterName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)) return null;
        var parts = windowsVersion.Split('.');
        if (parts.Length != 4 || !parts[2].All(char.IsDigit) || !parts[3].All(char.IsDigit) || parts[3].Length > 4) return null;
        var digits = parts[2] + parts[3].PadLeft(4, '0');
        if (digits.Length < 5) return null;
        digits = digits[^5..];
        return $"NVIDIA {int.Parse(digits[..3], CultureInfo.InvariantCulture)}.{digits[3..]}";
    }
}
