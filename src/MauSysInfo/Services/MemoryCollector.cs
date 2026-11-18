using System.Globalization;
using System.Text.RegularExpressions;

namespace MauSysInfo.Services;

public static partial class MemoryCollector
{
    public static PageData Collect()
    {
        var page = new PageData();
        var modules = Wmi.Query("SELECT BankLabel, DeviceLocator, Capacity, Manufacturer, PartNumber, SerialNumber, Speed, " +
                                "ConfiguredClockSpeed, ConfiguredVoltage, SMBIOSMemoryType, MemoryType, FormFactor, Attributes, " +
                                "DataWidth, TotalWidth FROM Win32_PhysicalMemory");
        var arrays = Wmi.Query("SELECT MemoryDevices, MaxCapacityEx FROM Win32_PhysicalMemoryArray WHERE Use = 3");
        var status = Native.MemoryStatus();

        // Summary
        var summary = page.AddBlock("Memory");
        string? type = modules.Select(m => TypeName(m.Long("SMBIOSMemoryType") ?? 0, m.Long("MemoryType") ?? 0))
                              .FirstOrDefault(t => t is not null);
        summary.Add("Type", type);

        ulong installedKb = 0;
        try { Native.GetPhysicallyInstalledSystemMemory(out installedKb); } catch { }
        long installed = installedKb > 0 ? (long)installedKb * 1024 : modules.Sum(m => m.Long("Capacity") ?? 0);
        summary.Add("Installed", installed > 0 ? Units.Bytes(installed) : null);
        if (status is { } st)
        {
            summary.Add("Usable by Windows", Units.Bytes(st.ullTotalPhys),
                "Installed memory minus what firmware and integrated graphics reserve");
        }

        long configured = modules.Select(m => m.Long("ConfiguredClockSpeed") ?? 0).Where(v => v > 0).DefaultIfEmpty(0).Min();
        if (configured > 0)
            summary.Add("Speed", Speed(configured),
                "Data rate the memory runs at now. DDR transfers twice per clock, so the DRAM clock is half of it.");

        if (JedecDefaultHint(type, configured) is { } hint)
            summary.Add("XMP / EXPO", "Probably off: memory runs at the standard speed",
                hint);

        int slots = (int)arrays.Sum(a => a.Long("MemoryDevices") ?? 0);
        summary.Add("Modules", slots >= modules.Count && slots > 0 ? $"{modules.Count} of {slots} slots used" : modules.Count.ToString());
        var channels = EstimateChannels(modules.Select(m => $"{m.Str("BankLabel")} {m.Str("DeviceLocator")}"));
        if (channels > 0)
            summary.Add("Channels", ChannelName(channels), "Estimated from the slot labels the firmware reports");
        long maxCapacity = arrays.Sum(a => a.Long("MaxCapacityEx") ?? 0); // KB
        if (maxCapacity > 0) summary.Add("Maximum supported", Units.Bytes(maxCapacity * 1024.0), "As reported by the motherboard firmware");

        // Live usage
        var usage = page.AddBlock("Usage");
        page.Live[LiveKeys.MemoryInUse] = usage.AddLive("In use");
        page.Live[LiveKeys.MemoryInUse].Meter = 0;
        page.Live[LiveKeys.MemoryAvailable] = usage.AddLive("Available");
        page.Live[LiveKeys.MemoryCommitted] = usage.AddLive("Committed",
            "Memory programs have reserved, against the limit of RAM plus page file");

        // One card per module
        foreach (var m in modules.OrderBy(m => m.Str("BankLabel")).ThenBy(m => m.Str("DeviceLocator")))
        {
            string slot = string.Join(" / ", new[] { m.Str("BankLabel"), m.Str("DeviceLocator") }.Where(s => s is not null));
            string? part = m.Str("PartNumber");
            var card = page.AddBlock(slot.Length > 0 ? slot : "Module", part);
            long size = m.Long("Capacity") ?? 0;
            card.Add("Size", size > 0 ? Units.Bytes(size) : null);
            card.Add("Type", TypeName(m.Long("SMBIOSMemoryType") ?? 0, m.Long("MemoryType") ?? 0));
            card.Add("Form factor", FormFactor(m.Long("FormFactor") ?? 0));
            card.Add("Manufacturer", ManufacturerName(m.Str("Manufacturer"), part));
            card.Add("Part number", part);
            card.Add("Serial number", m.Str("SerialNumber"));
            long rated = m.Long("Speed") ?? 0;
            long now = m.Long("ConfiguredClockSpeed") ?? 0;
            card.Add("Rated speed", rated > 0 ? Speed(rated) : null,
                "The module's standard (JEDEC) speed. XMP / EXPO profiles can run it faster.");
            if (now > 0 && now != rated) card.Add("Running at", Speed(now));
            long mv = m.Long("ConfiguredVoltage") ?? 0;
            card.Add("Voltage", mv > 0 ? $"{(mv / 1000.0).ToString("0.###", CultureInfo.InvariantCulture)} V" : null);
            long ranks = m.Long("Attributes") ?? 0;
            card.Add("Ranks", ranks switch { 1 => "Single rank", 2 => "Dual rank", 4 => "Quad rank", > 0 => $"{ranks} ranks", _ => null });
            long data = m.Long("DataWidth") ?? 0, total = m.Long("TotalWidth") ?? 0;
            if (data > 0)
                card.Add("Error correction", total > data ? "ECC" : "None (non-ECC)");
        }

        return page.WithoutEmptyBlocks();
    }

    /// <summary>
    /// Memory at the plain JEDEC base speed usually means the kit's XMP / EXPO profile isn't enabled. Kits rated
    /// at exactly that speed exist, so this is phrased as a hint.
    /// </summary>
    public static string? JedecDefaultHint(string? type, long configuredMts)
    {
        long baseSpeed = type switch { "DDR4" => 2400, "DDR5" => 4800, "DDR3" => 1333, _ => 0 };
        if (baseSpeed == 0 || configuredMts <= 0 || configuredMts > baseSpeed) return null;
        return $"{type} at {configuredMts} MT/s is the standard (JEDEC) speed. If your kit is rated faster, " +
               "enable its XMP / EXPO / DOCP profile in the BIOS to run at the rated speed.";
    }

    public static string Speed(long megaTransfers) =>
        $"{megaTransfers} MT/s (DRAM clock {(megaTransfers / 2.0).ToString("0.#", CultureInfo.InvariantCulture)} MHz)";

    /// <summary>SMBIOS memory device type (Win32_PhysicalMemory.SMBIOSMemoryType), falling back to the older MemoryType.</summary>
    public static string? TypeName(long smbiosType, long legacyType) => smbiosType switch
    {
        18 => "DDR",
        19 => "DDR2",
        20 => "DDR",
        21 => "DDR2",
        22 => "DDR2 FB-DIMM",
        24 => "DDR3",
        26 => "DDR4",
        27 => "LPDDR",
        28 => "LPDDR2",
        29 => "LPDDR3",
        30 => "LPDDR4",
        34 => "DDR5",
        35 => "LPDDR5",
        _ => legacyType switch { 20 => "DDR", 21 => "DDR2", 24 => "DDR3", 26 => "DDR4", 34 => "DDR5", _ => null },
    };

    public static string? FormFactor(long code) => code switch
    {
        8 => "DIMM",
        12 => "SO-DIMM",
        13 => "Row of chips (soldered)",
        _ => null,
    };

    /// <summary>
    /// Counts distinct channels named in slot labels ("P0 CHANNEL A", "ChannelB-DIMM0", "DIMM_A1", "Controller0-ChannelA").
    /// Returns 0 when the labels say nothing about channels.
    /// </summary>
    public static int EstimateChannels(IEnumerable<string> labels)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var label in labels)
        {
            var m = ChannelRegex().Match(label);
            if (!m.Success) m = DimmLetterRegex().Match(label);
            if (m.Success) found.Add(m.Groups["ch"].Value);
        }
        return found.Count;
    }

    [GeneratedRegex(@"CHANNEL\s*[-_]?\s*(?<ch>[A-H])\b", RegexOptions.IgnoreCase)]
    private static partial Regex ChannelRegex();

    [GeneratedRegex(@"DIMM[\s_-]?(?<ch>[A-H])\d", RegexOptions.IgnoreCase)]
    private static partial Regex DimmLetterRegex();

    public static string ChannelName(int count) => count switch
    {
        1 => "Single",
        2 => "Dual",
        3 => "Triple",
        4 => "Quad",
        6 => "Hexa",
        8 => "Octa",
        _ => count.ToString(),
    };

    /// <summary>
    /// Firmware often reports the maker as a JEDEC code ("80CE") or "Unknown"; turn it into a name, falling
    /// back to the part number prefix the big brands use.
    /// </summary>
    public static string? ManufacturerName(string? reported, string? partNumber)
    {
        if (reported is not null)
        {
            string hex = reported.Replace(" ", "").ToUpperInvariant();
            foreach (var (codes, name) in JedecIds)
                if (codes.Any(c => hex.StartsWith(c, StringComparison.Ordinal)))
                    return name;
            if (!IsHexCode(hex)) return reported;
        }
        if (partNumber is not null)
        {
            foreach (var (prefix, name) in PartPrefixes)
                if (partNumber.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return $"{name} (from part number)";
        }
        return reported is not null && !IsHexCode(reported) ? reported : null;
    }

    private static bool IsHexCode(string value) => value.Length >= 4 && value.All(Uri.IsHexDigit);

    // JEDEC JEP106 manufacturer IDs in the forms Windows reports (with and without the continuation/parity byte).
    private static readonly (string[] Codes, string Name)[] JedecIds =
    [
        (["80CE", "CE00", "00CE"], "Samsung"),
        (["80AD", "AD00", "00AD"], "SK Hynix"),
        (["802C", "2C00", "002C"], "Micron"),
        (["859B", "9B05", "059B"], "Crucial"),
        (["0198", "9801"], "Kingston"),
        (["029E", "9E02"], "Corsair"),
        (["04CD", "CD04"], "G.Skill"),
        (["04CB", "CB04"], "ADATA"),
        (["04EF", "EF04"], "Team Group"),
        (["830B", "0B03"], "Nanya"),
        (["0194", "9401"], "Mushkin"),
        (["0443", "4304"], "Ramaxel"),
        (["8551", "5105"], "Qimonda"),
        (["8A45", "4508"], "Patriot"),
    ];

    private static readonly (string Prefix, string Name)[] PartPrefixes =
    [
        ("CM", "Corsair"),
        ("F4-", "G.Skill"),
        ("F5-", "G.Skill"),
        ("F3-", "G.Skill"),
        ("KF", "Kingston"),
        ("KHX", "Kingston"),
        ("KVR", "Kingston"),
        ("BL", "Crucial"),
        ("CT", "Crucial"),
        ("M378", "Samsung"),
        ("M471", "Samsung"),
        ("M323", "Samsung"),
        ("M425", "Samsung"),
        ("HMA", "SK Hynix"),
        ("HMC", "SK Hynix"),
        ("HMT", "SK Hynix"),
        ("MTA", "Micron"),
        ("MTC", "Micron"),
        ("TF", "Team Group"),
        ("TLZ", "Team Group"),
        ("TED", "Team Group"),
        ("AX4U", "ADATA"),
        ("AX5U", "ADATA"),
        ("PSD", "Patriot"),
        ("PVS", "Patriot"),
    ];
}
