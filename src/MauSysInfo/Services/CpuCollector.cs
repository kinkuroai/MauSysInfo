using MauSysInfo.Models;
using Microsoft.Win32;

namespace MauSysInfo.Services;

public static class CpuCollector
{
    public static PageData Collect()
    {
        var page = new PageData();
        var id = Cpuid.ReadLocal();
        var topology = CpuTopology.ReadLocal();
        var wmi = Wmi.First("SELECT Name, Manufacturer, MaxClockSpeed, SocketDesignation, VirtualizationFirmwareEnabled, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor");
        int sockets = Math.Max(1, Wmi.Query("SELECT DeviceID FROM Win32_Processor").Count);

        // Processor
        var cpu = page.AddBlock("Processor", wide: true);
        string name = id?.Brand is { Length: > 0 } b ? b : wmi?.Str("Name") ?? "Unknown processor";
        cpu.Add("Name", name);
        cpu.Add("Vendor", id is not null ? CpuNames.VendorName(id.Vendor) : wmi?.Str("Manufacturer"));
        if (id is not null)
        {
            var codename = CpuNames.Lookup(id.Vendor, id.Family, id.Model, id.Stepping);
            cpu.Add("Codename", codename?.Codename);
            cpu.Add("Microarchitecture", codename?.Microarchitecture);
            cpu.Add("Technology", codename?.Process, "Manufacturing process, from the CPU's family and model");
            cpu.Add("Family / Model / Stepping", $"{Units.Hex(id.Family)}h / {Units.Hex(id.Model)}h / {id.Stepping}",
                $"Hex, with the extended fields folded in. Base family {Units.Hex(id.BaseFamily)}, ext. family {Units.Hex(id.ExtFamily)}, " +
                $"base model {Units.Hex(id.BaseModel)}, ext. model {Units.Hex(id.ExtModel)}");
            cpu.Add("CPUID signature", id.SignatureHex);
        }
        cpu.Add("Microcode revision", MicrocodeRevision());
        cpu.Add("Socket", wmi?.Str("SocketDesignation"));
        if (id is not null)
            cpu.Add("Instructions", string.Join(", ", id.InstructionSets));

        // Clocks
        var clocks = page.AddBlock("Clocks");
        long baseMhz = wmi?.Long("MaxClockSpeed") ?? 0;
        clocks.Add("Base clock", baseMhz > 0 ? Units.Clock(baseMhz) : null, "Rated base clock (Windows' processor information)");
        page.Live[LiveKeys.CpuClock] = clocks.AddLive("Current clock",
            "Average effective clock of all threads, measured the way Task Manager does. Boost clocks show above base.");
        page.Live[LiveKeys.CpuUsage] = clocks.AddLive("Usage", "Processor utility, as in Task Manager");
        page.Live[LiveKeys.CpuUsage].Meter = 0;

        // Cores
        var cores = page.AddBlock("Cores and threads");
        if (topology is { Cores.Count: > 0 })
        {
            cores.Add("Cores", topology.Cores.Count.ToString());
            if (topology.IsHybrid)
                cores.Add("Core types", $"{topology.PerformanceCores} performance + {topology.EfficiencyCores} efficiency",
                    "Hybrid CPU: Windows schedules demanding work on the performance cores first");
            cores.Add("Threads", topology.ThreadCount.ToString());
            cores.Add("Simultaneous multithreading", topology.Cores.Any(c => c.Smt) ? "Enabled" : "Off / not supported",
                "Hyper-Threading on Intel, SMT on AMD");
            cores.Add("Sockets", Math.Max(sockets, topology.Packages).ToString());
            if (topology.NumaNodes > 1) cores.Add("NUMA nodes", topology.NumaNodes.ToString());
        }
        else
        {
            cores.Add("Cores", wmi?.Long("NumberOfCores")?.ToString());
            cores.Add("Threads", wmi?.Long("NumberOfLogicalProcessors")?.ToString());
        }
        bool? vtFirmware = wmi?.Bool("VirtualizationFirmwareEnabled");
        bool hypervisor = id?.HypervisorPresent ?? false;
        cores.Add("Virtualization", hypervisor
                ? "In use (a hypervisor is running: Hyper-V, WSL 2 or virtualization-based security)"
                : vtFirmware switch { true => "Enabled in firmware", false => "Disabled in firmware (BIOS/UEFI setup)", _ => null },
            id?.IsAmd == true ? "AMD-V / SVM" : "Intel VT-x");

        // Caches
        if (topology is { Caches.Count: > 0 })
        {
            var caches = page.AddBlock("Caches");
            foreach (var cache in topology.CacheSummaries())
            {
                string shared = cache.SharedBy switch
                {
                    <= 1 => "",
                    _ when cache.SharedBy == topology.ThreadCount => "Shared by all threads",
                    _ => $"Each shared by {cache.SharedBy} threads",
                };
                caches.Add(cache.Name, cache.Describe(), shared.Length > 0 ? shared : null);
            }
            long l3 = topology.Caches.Where(c => c.Level == 3).Sum(c => c.Size);
            if (l3 > 0) caches.Add("Total L3", Units.Bytes(l3));
        }

        // Live per-thread tiles
        if (topology is { Cores.Count: > 0 })
        {
            var tiles = new List<ThreadTile>();
            foreach (var core in topology.Cores)
            {
                string? kind = topology.IsHybrid ? (topology.IsPerformanceCore(core) ? "P" : "E") : null;
                foreach (var thread in core.Threads)
                {
                    int index = thread.Group * 64 + thread.Number;
                    var tile = new ThreadTile(index, $"Core {core.Index}", kind);
                    tiles.Add(tile);
                    page.Threads.Add((tile, thread));
                }
            }
            page.Blocks.Add(new ThreadsBlock("Threads", tiles.OrderBy(t => t.Index)) { Wide = true });
        }

        return page.WithoutEmptyBlocks();
    }

    /// <summary>
    /// The microcode revision Windows loaded, from the registry. Intel stores it in the high 4 bytes of an
    /// 8-byte value, AMD in the first 4 bytes.
    /// </summary>
    internal static string? MicrocodeRevision()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return key?.GetValue("Update Revision") is byte[] bytes ? FormatMicrocode(bytes) : null;
        }
        catch
        {
            return null;
        }
    }

    internal static string? FormatMicrocode(byte[] bytes)
    {
        uint revision = 0;
        if (bytes.Length >= 8) revision = BitConverter.ToUInt32(bytes, 4);
        if (revision == 0 && bytes.Length >= 4) revision = BitConverter.ToUInt32(bytes, 0);
        return revision == 0 ? null : $"0x{revision:X}";
    }
}
