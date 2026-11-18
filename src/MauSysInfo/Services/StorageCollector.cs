using System.Globalization;

namespace MauSysInfo.Services;

public static class StorageCollector
{
    public static PageData Collect()
    {
        var page = new PageData();
        bool elevated = ShellActions.IsElevated;

        var disks = Wmi.Query("SELECT DeviceId, FriendlyName, MediaType, BusType, Size, HealthStatus, FirmwareVersion, SerialNumber, SpindleSpeed " +
                              "FROM MSFT_PhysicalDisk", Wmi.Storage);
        var styles = Wmi.Query("SELECT Number, PartitionStyle FROM MSFT_Disk", Wmi.Storage)
                        .Where(d => d.Long("Number") is not null)
                        .GroupBy(d => d.Long("Number")!.Value.ToString(CultureInfo.InvariantCulture))
                        .ToDictionary(g => g.Key, g => g.First().Long("PartitionStyle") ?? 0);
        var letters = Wmi.Query("SELECT DiskNumber, DriveLetter FROM MSFT_Partition", Wmi.Storage)
                         .Where(p => p.Raw("DriveLetter") is char c && c != '\0')
                         .GroupBy(p => (p.Long("DiskNumber") ?? -1).ToString(CultureInfo.InvariantCulture))
                         .ToDictionary(g => g.Key, g => g.Select(p => (char)p.Raw("DriveLetter")!).OrderBy(c => c).ToList());
        var reliability = elevated
            ? Wmi.Query("SELECT DeviceId, Temperature, TemperatureMax, Wear, PowerOnHours, ReadErrorsTotal, WriteErrorsTotal, StartStopCycleCount " +
                        "FROM MSFT_StorageReliabilityCounter", Wmi.Storage)
                 .Where(r => Id(r) is not null)
                 .GroupBy(r => Id(r)!)
                 .ToDictionary(g => g.Key, g => g.First())
            : [];

        var drives = DriveInfo.GetDrives().Where(d => SafeReady(d)).ToDictionary(d => char.ToUpperInvariant(d.Name[0]));
        var shownLetters = new HashSet<char>();

        foreach (var disk in disks.OrderBy(d => int.TryParse(Id(d), out var n) ? n : int.MaxValue))
        {
            string id = Id(disk) ?? "?";
            string model = disk.Str("FriendlyName") ?? "Disk";
            var card = page.AddBlock($"Disk {id}", model);
            card.Add("Model", model);
            string? media = MediaType(disk.Long("MediaType") ?? 0);
            string? bus = BusType(disk.Long("BusType") ?? 0);
            card.Add("Type", string.Join(" ", new[] { bus is "NVMe" ? "NVMe" : null, media }.Where(s => s is not null)) is { Length: > 0 } t ? t : null);
            card.Add("Interface", bus);
            long size = disk.Long("Size") ?? 0;
            card.Add("Capacity", size > 0 ? Units.DriveSize(size) : null);
            if ((disk.Long("SpindleSpeed") ?? 0) is > 0 and < 100_000 and var rpm) card.Add("Rotation speed", $"{rpm} RPM");
            card.Add("Firmware", disk.Str("FirmwareVersion"));
            card.Add("Serial number", disk.Str("SerialNumber")?.TrimEnd('.'));
            card.Add("Health", (disk.Long("HealthStatus") ?? -1) switch { 0 => "Healthy", 1 => "Warning", 2 => "Unhealthy", _ => null },
                "Windows' own health verdict for the drive");
            if (styles.TryGetValue(id, out var style))
                card.Add("Partition style", style switch { 1 => "MBR", 2 => "GPT", _ => null });

            if (reliability.TryGetValue(id, out var r))
            {
                if ((r.Long("Temperature") ?? 0) is > 0 and var temp)
                    card.Add("Temperature", $"{temp} °C" + ((r.Long("TemperatureMax") ?? 0) is > 0 and var max ? $" (rated max {max} °C)" : ""));
                if (r.Long("Wear") is { } wear && (media == "SSD" || bus == "NVMe"))
                    card.Add("Wear", $"{wear} % of rated life used", "From the drive's own counters. 100 % is the maker's rated endurance, not a failure.",
                        meter: Math.Clamp(wear / 100.0, 0, 1));
                if ((r.Long("PowerOnHours") ?? 0) is > 0 and var hours)
                    card.Add("Powered on", $"{hours:N0} hours ({(hours / 24.0).ToString("0", CultureInfo.InvariantCulture)} days)");
                if ((r.Long("StartStopCycleCount") ?? 0) is > 0 and var cycles) card.Add("Power cycles", cycles.ToString("N0"));
                long errors = (r.Long("ReadErrorsTotal") ?? 0) + (r.Long("WriteErrorsTotal") ?? 0);
                if (r.Raw("ReadErrorsTotal") is not null) card.Add("Read/write errors", errors.ToString("N0"));
            }
            else if (!elevated)
            {
                card.Add("Temperature and wear", "Restart as administrator to read them");
            }

            if (letters.TryGetValue(id, out var diskLetters))
            {
                foreach (var letter in diskLetters)
                {
                    if (!drives.TryGetValue(char.ToUpperInvariant(letter), out var drive)) continue;
                    shownLetters.Add(char.ToUpperInvariant(letter));
                    AddVolume(card, drive);
                }
            }
        }

        // Volumes not tied to a physical disk above (network drives, USB sticks on older drivers, RAM disks...)
        var other = drives.Values.Where(d => !shownLetters.Contains(char.ToUpperInvariant(d.Name[0]))).ToList();
        if (other.Count > 0)
        {
            var card = page.AddBlock(disks.Count > 0 ? "Other volumes" : "Volumes");
            foreach (var drive in other) AddVolume(card, drive);
        }

        return page.WithoutEmptyBlocks();
    }

    /// <summary>Disk number as text. Not Str(): "0" is a real disk number, not a firmware placeholder.</summary>
    private static string? Id(System.Management.ManagementBaseObject obj) => obj.Raw("DeviceId")?.ToString()?.Trim();

    private static void AddVolume(Models.FieldsBlock card, DriveInfo drive)
    {
        try
        {
            string label = drive.Name.TrimEnd('\\') + (string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "" : $" {drive.VolumeLabel}");
            long total = drive.TotalSize, free = drive.AvailableFreeSpace;
            string kind = drive.DriveType == DriveType.Network ? ", network" : drive.DriveType == DriveType.Removable ? ", removable" : "";
            card.Add(label, $"{Units.Bytes(free)} free of {Units.Bytes(total)} · {drive.DriveFormat}{kind}",
                meter: total > 0 ? 1 - (double)free / total : null);
        }
        catch { }
    }

    private static bool SafeReady(DriveInfo d)
    {
        try { return d.IsReady && d.DriveType != DriveType.CDRom; }
        catch { return false; }
    }

    public static string? MediaType(long code) => code switch { 3 => "HDD", 4 => "SSD", 5 => "SCM", _ => null };

    /// <summary>MSFT_PhysicalDisk.BusType (STORAGE_BUS_TYPE).</summary>
    public static string? BusType(long code) => code switch
    {
        1 => "SCSI",
        2 => "ATAPI",
        3 => "ATA",
        4 => "FireWire",
        5 => "SSA",
        6 => "Fibre Channel",
        7 => "USB",
        8 => "RAID",
        9 => "iSCSI",
        10 => "SAS",
        11 => "SATA",
        12 => "SD card",
        13 => "MMC",
        14 => "Virtual",
        15 => "File-backed virtual",
        16 => "Storage Spaces",
        17 => "NVMe",
        18 => "SCM",
        19 => "UFS",
        _ => null,
    };
}
