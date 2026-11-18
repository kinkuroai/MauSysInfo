using System.Globalization;

namespace MauSysInfo.Services;

/// <summary>Number formatting shared by every page and the report.</summary>
public static class Units
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Binary units (1 GB = 1024³ bytes), as Windows shows memory and file sizes. "16 GB", "1.5 MB".</summary>
    public static string Bytes(double bytes)
    {
        string[] units = ["bytes", "KB", "MB", "GB", "TB", "PB"];
        int i = 0;
        while (Math.Abs(bytes) >= 1024 && i < units.Length - 1) { bytes /= 1024; i++; }
        if (i == 0) return $"{bytes:0} bytes";
        return $"{Trim(bytes)} {units[i]}";
    }

    /// <summary>Decimal units (1 GB = 1000³ bytes), as drive makers label capacity. "512 GB", "2 TB".</summary>
    public static string DecimalBytes(double bytes)
    {
        string[] units = ["bytes", "KB", "MB", "GB", "TB", "PB"];
        int i = 0;
        while (Math.Abs(bytes) >= 1000 && i < units.Length - 1) { bytes /= 1000; i++; }
        if (i == 0) return $"{bytes:0} bytes";
        return $"{Trim(bytes)} {units[i]}";
    }

    /// <summary>Drive capacity as labelled on the box plus what Windows shows: "512.1 GB (476.9 GB in Windows)".</summary>
    public static string DriveSize(double bytes) => $"{DecimalBytes(bytes)} ({Bytes(bytes)} in Windows)";

    /// <summary>3 significant-ish digits: 16, 15.9, 476.9, 1.82.</summary>
    private static string Trim(double value)
    {
        if (Math.Abs(value - Math.Round(value)) < 0.005) return Math.Round(value).ToString("0", Inv);
        return value >= 100 ? value.ToString("0.#", Inv) : value.ToString("0.##", Inv);
    }

    /// <summary>"3701 MHz" below 1 GHz shown as MHz, above as "4.85 GHz".</summary>
    public static string Clock(double mhz)
    {
        if (mhz <= 0 || double.IsNaN(mhz)) return "—";
        return mhz >= 1000 ? $"{(mhz / 1000).ToString("0.00", Inv)} GHz" : $"{mhz.ToString("0", Inv)} MHz";
    }

    public static string Percent(double fraction) => $"{(fraction * 100).ToString("0", Inv)} %";

    /// <summary>"3 days, 4 h 05 min" / "4 h 05 min" / "12 min".</summary>
    public static string Duration(TimeSpan span)
    {
        if (span.TotalMinutes < 1) return $"{(int)span.TotalSeconds} s";
        var hm = span.TotalHours >= 1 ? $"{span.Hours} h {span.Minutes:00} min" : $"{span.Minutes} min";
        return span.Days switch
        {
            0 => hm,
            1 => $"1 day, {hm}",
            _ => $"{span.Days} days, {hm}",
        };
    }

    /// <summary>Network link speed in bits per second: "1 Gbps", "866.7 Mbps".</summary>
    public static string LinkSpeed(long bitsPerSecond)
    {
        if (bitsPerSecond <= 0) return "—";
        if (bitsPerSecond >= 1_000_000_000) return $"{Trim(bitsPerSecond / 1e9)} Gbps";
        if (bitsPerSecond >= 1_000_000) return $"{Trim(bitsPerSecond / 1e6)} Mbps";
        return $"{Trim(bitsPerSecond / 1e3)} Kbps";
    }

    /// <summary>Hex like CPU-Z: "B7", "1A".</summary>
    public static string Hex(int value) => value.ToString("X", Inv);
}
