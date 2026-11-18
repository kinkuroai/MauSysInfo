using System.Management;

namespace MauSysInfo.Services;

/// <summary>Small, failure-tolerant WMI helpers: a missing class or access denied just yields no rows.</summary>
public static class Wmi
{
    public const string Cimv2 = @"root\cimv2";
    public const string Storage = @"root\Microsoft\Windows\Storage";
    public const string RootWmi = @"root\wmi";
    public const string DeviceGuard = @"root\Microsoft\Windows\DeviceGuard";

    public static List<ManagementBaseObject> Query(string wql, string scope = Cimv2)
    {
        var list = new List<ManagementBaseObject>();
        try
        {
            var options = new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(15), ReturnImmediately = true, Rewindable = false };
            using var searcher = new ManagementObjectSearcher(new ManagementScope(scope), new ObjectQuery(wql), options);
            foreach (var item in searcher.Get()) list.Add(item);
        }
        catch
        {
            // Class not present on this Windows edition, access denied, WMI repository broken: show what we can.
        }
        return list;
    }

    public static ManagementBaseObject? First(string wql, string scope = Cimv2) => Query(wql, scope).FirstOrDefault();

    public static object? Raw(this ManagementBaseObject obj, string property)
    {
        try { return obj[property]; }
        catch { return null; }
    }

    /// <summary>Trimmed string, or null for empty values and the placeholders firmware likes to fill in.</summary>
    public static string? Str(this ManagementBaseObject obj, string property) => Clean(obj.Raw(property)?.ToString());

    public static long? Long(this ManagementBaseObject obj, string property)
    {
        var raw = obj.Raw(property);
        if (raw is null) return null;
        try { return Convert.ToInt64(raw); }
        catch { return null; }
    }

    public static bool? Bool(this ManagementBaseObject obj, string property) => obj.Raw(property) as bool?;

    /// <summary>
    /// SMBIOS strings are often left as OEM placeholders; treat those as "not reported" so the card skips the line.
    /// </summary>
    public static string? Clean(string? value)
    {
        if (value is null) return null;
        value = value.Trim().Trim('\0').Trim();
        if (value.Length == 0) return null;
        string[] placeholders =
        [
            "To Be Filled By O.E.M.", "To be filled by O.E.M.", "Default string", "System Product Name",
            "System manufacturer", "System Version", "Not Applicable", "None", "Unknown", "O.E.M.", "OEM",
            "Not Specified", "Base Board Serial Number", "Chassis Serial Number", "0", "00000000",
        ];
        return placeholders.Any(p => p.Equals(value, StringComparison.OrdinalIgnoreCase)) ? null : value;
    }

    /// <summary>WMI datetime ("20240315000000.000000+000") to a date.</summary>
    public static DateTime? Date(this ManagementBaseObject obj, string property)
    {
        var raw = obj.Raw(property)?.ToString();
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try { return ManagementDateTimeConverter.ToDateTime(raw); }
        catch { return null; }
    }
}
