using System.Buffers.Binary;
using System.Text;
using MauSysInfo.Services;

namespace MauSysInfo.Tests;

public class CpuidTests
{
    [Theory]
    // Ryzen 9 5900X: base family F + ext family A = 19h, model 21h, stepping 2
    [InlineData(0x00A20F12u, 0x19, 0x21, 2)]
    // Core i9-13900K: family 6, ext model B, model 7 = B7h, stepping 1
    [InlineData(0x000B0671u, 0x6, 0xB7, 1)]
    // Core i7-8700K: family 6, model 9Eh, stepping 10 (Coffee Lake)
    [InlineData(0x000906EAu, 0x6, 0x9E, 10)]
    // Ryzen 7 9700X (Zen 5): family 1Ah, model 44h
    [InlineData(0x00B40F40u, 0x1A, 0x44, 0)]
    public void Signature_folds_extended_fields(uint eax, int family, int model, int stepping)
    {
        var sig = Cpuid.Signature(eax);
        Assert.Equal(family, sig.Family);
        Assert.Equal(model, sig.Model);
        Assert.Equal(stepping, sig.Stepping);
    }

    [Fact]
    public void Decode_reads_vendor_brand_and_features_from_registers()
    {
        static uint Str(string s, int offset) => BitConverter.ToUInt32(Encoding.ASCII.GetBytes(s.PadRight(48, '\0')), offset);
        const string brand = "AMD Ryzen 9 5900X 12-Core Processor            ";
        var table = new Dictionary<uint, CpuidRegs>
        {
            // "AuthenticAMD" is EBX, EDX, ECX
            [0] = new(0x10, Str("Auth", 0), Str("cAMD", 0), Str("enti", 0)),
            // SSE3, SSSE3, SSE4.1/4.2, AES, AVX, FMA, hypervisor | MMX, SSE, SSE2
            [1] = new(0x00A20F12, 0, (1u << 0) | (1u << 9) | (1u << 19) | (1u << 20) | (1u << 25) | (1u << 28) | (1u << 12) | (1u << 31),
                      (1u << 23) | (1u << 25) | (1u << 26)),
            [7] = new(0, (1u << 5) | (1u << 3) | (1u << 8) | (1u << 29), 0, 0), // AVX2, BMI1, BMI2, SHA
            [0x80000000] = new(0x80000008, 0, 0, 0),
            [0x80000001] = new(0, 0, (1u << 2) | (1u << 6), 1u << 29), // AMD-V, SSE4A | x86-64
            [0x80000002] = new(Str(brand, 0), Str(brand, 4), Str(brand, 8), Str(brand, 12)),
            [0x80000003] = new(Str(brand, 16), Str(brand, 20), Str(brand, 24), Str(brand, 28)),
            [0x80000004] = new(Str(brand, 32), Str(brand, 36), Str(brand, 40), Str(brand, 44)),
        };

        var id = Cpuid.Decode((leaf, _) => table.GetValueOrDefault(leaf));

        Assert.Equal("AuthenticAMD", id.Vendor);
        Assert.True(id.IsAmd);
        Assert.Equal("AMD Ryzen 9 5900X 12-Core Processor", id.Brand);
        Assert.Equal(0x19, id.Family);
        Assert.Equal(0x21, id.Model);
        Assert.True(id.HypervisorPresent);
        Assert.True(id.HardwareVirtualization);
        Assert.Equal("A20F12", id.SignatureHex);
        foreach (var set in new[] { "MMX", "SSE", "SSE2", "SSE3", "SSSE3", "SSE4.1", "SSE4.2", "SSE4A", "x86-64", "AMD-V", "AES", "AVX", "AVX2", "FMA3", "SHA", "BMI1", "BMI2" })
            Assert.Contains(set, id.InstructionSets);
        Assert.DoesNotContain("AVX-512F", id.InstructionSets);
        Assert.DoesNotContain("VT-x", id.InstructionSets);
    }

    [Theory]
    [InlineData("AuthenticAMD", 0x19, 0x21, 2, "Vermeer")]
    [InlineData("AuthenticAMD", 0x19, 0x61, 2, "Raphael")]
    [InlineData("AuthenticAMD", 0x1A, 0x44, 0, "Granite Ridge")]
    [InlineData("GenuineIntel", 6, 0xB7, 1, "Raptor Lake-S")]
    [InlineData("GenuineIntel", 6, 0x97, 2, "Alder Lake-S")]
    [InlineData("GenuineIntel", 6, 0x9E, 9, "Kaby Lake")]
    [InlineData("GenuineIntel", 6, 0x9E, 10, "Coffee Lake")]
    public void Codenames(string vendor, int family, int model, int stepping, string expected) =>
        Assert.Equal(expected, CpuNames.Lookup(vendor, family, model, stepping)?.Codename);

    [Fact]
    public void Unknown_signature_has_no_codename() => Assert.Null(CpuNames.Lookup("GenuineIntel", 6, 0x01));

    [Theory]
    [InlineData(new byte[] { 0x13, 0x12, 0x20, 0x0A }, "0xA201213")]                       // AMD: first 4 bytes
    [InlineData(new byte[] { 0, 0, 0, 0, 0x29, 0x01, 0, 0 }, "0x129")]                      // Intel: high 4 bytes
    [InlineData(new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 }, null)]
    public void Microcode(byte[] raw, string? expected) => Assert.Equal(expected, CpuCollector.FormatMicrocode(raw));
}

public class TopologyTests
{
    /// <summary>Builds SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX records the way Windows lays them out on x64.</summary>
    private static byte[] Core(byte efficiency, bool smt, ulong mask)
    {
        var rec = new byte[48];
        BinaryPrimitives.WriteInt32LittleEndian(rec, 0);
        BinaryPrimitives.WriteInt32LittleEndian(rec.AsSpan(4), rec.Length);
        rec[8] = (byte)(smt ? 1 : 0);
        rec[9] = efficiency;
        BinaryPrimitives.WriteUInt16LittleEndian(rec.AsSpan(8 + 22), 1);
        BinaryPrimitives.WriteUInt64LittleEndian(rec.AsSpan(8 + 24), mask);
        return rec;
    }

    private static byte[] Cache(byte level, byte assoc, ushort line, uint size, int type, ulong mask)
    {
        var rec = new byte[56];
        BinaryPrimitives.WriteInt32LittleEndian(rec, 2);
        BinaryPrimitives.WriteInt32LittleEndian(rec.AsSpan(4), rec.Length);
        rec[8] = level;
        rec[9] = assoc;
        BinaryPrimitives.WriteUInt16LittleEndian(rec.AsSpan(10), line);
        BinaryPrimitives.WriteUInt32LittleEndian(rec.AsSpan(12), size);
        BinaryPrimitives.WriteInt32LittleEndian(rec.AsSpan(16), type);
        BinaryPrimitives.WriteUInt16LittleEndian(rec.AsSpan(8 + 30), 1);
        BinaryPrimitives.WriteUInt64LittleEndian(rec.AsSpan(8 + 32), mask);
        return rec;
    }

    private static byte[] Package()
    {
        var rec = new byte[48];
        BinaryPrimitives.WriteInt32LittleEndian(rec, 3);
        BinaryPrimitives.WriteInt32LittleEndian(rec.AsSpan(4), rec.Length);
        return rec;
    }

    [Fact]
    public void Hybrid_cpu_counts_performance_and_efficiency_cores()
    {
        // 2 P-cores with SMT (threads 0-1, 2-3) and 4 E-cores (threads 4..7)
        var data = new List<byte>();
        data.AddRange(Core(1, true, 0b11));
        data.AddRange(Core(1, true, 0b1100));
        for (int i = 4; i < 8; i++) data.AddRange(Core(0, false, 1UL << i));
        data.AddRange(Cache(1, 12, 64, 48 * 1024, 2, 0b11));
        data.AddRange(Cache(1, 12, 64, 48 * 1024, 2, 0b1100));
        data.AddRange(Cache(3, 12, 64, 36 * 1024 * 1024, 0, 0xFF));
        data.AddRange(Package());

        var t = CpuTopology.Parse(data.ToArray());

        Assert.Equal(6, t.Cores.Count);
        Assert.Equal(8, t.ThreadCount);
        Assert.True(t.IsHybrid);
        Assert.Equal(2, t.PerformanceCores);
        Assert.Equal(4, t.EfficiencyCores);
        Assert.Equal(1, t.Packages);
        Assert.Equal(new LogicalCpu(0, 2), t.Cores[1].Threads[0]);
        Assert.Equal("0,2", t.Cores[1].Threads[0].CounterInstance);

        var caches = t.CacheSummaries();
        Assert.Equal("L1 Data", caches[0].Name);
        Assert.Equal("2 x 48 KB, 12-way, 64-byte line", caches[0].Describe());
        Assert.Equal(8, caches.Single(c => c.Level == 3).SharedBy);
    }

    [Fact]
    public void Uniform_cpu_is_not_hybrid()
    {
        var data = new List<byte>();
        for (int i = 0; i < 4; i++) data.AddRange(Core(0, true, 0b11UL << (i * 2)));
        var t = CpuTopology.Parse(data.ToArray());
        Assert.False(t.IsHybrid);
        Assert.Equal(4, t.PerformanceCores);
        Assert.Equal(0, t.EfficiencyCores);
    }

    [Fact]
    public void Truncated_buffer_does_not_throw()
    {
        var data = Core(0, false, 1).Take(20).ToArray();
        var t = CpuTopology.Parse(data);
        Assert.Empty(t.Cores);
    }
}

public class FormattingTests
{
    [Theory]
    [InlineData(16L * 1024 * 1024 * 1024, "16 GB")]
    [InlineData(1536L * 1024, "1.5 MB")]
    [InlineData(512, "512 bytes")]
    [InlineData(32L * 1024, "32 KB")]
    public void Binary_bytes(long bytes, string expected) => Assert.Equal(expected, Units.Bytes(bytes));

    [Fact]
    public void Drive_size_shows_box_and_windows_units() =>
        Assert.Equal("512.1 GB (476.9 GB in Windows)", Units.DriveSize(512110190592));

    [Theory]
    [InlineData(3701, "3.70 GHz")]
    [InlineData(800, "800 MHz")]
    [InlineData(0, "—")]
    public void Clocks(double mhz, string expected) => Assert.Equal(expected, Units.Clock(mhz));

    [Fact]
    public void Durations()
    {
        Assert.Equal("12 min", Units.Duration(TimeSpan.FromMinutes(12)));
        Assert.Equal("4 h 05 min", Units.Duration(new TimeSpan(4, 5, 0)));
        Assert.Equal("1 day, 2 h 00 min", Units.Duration(new TimeSpan(1, 2, 0, 0)));
        Assert.Equal("3 days, 0 h 00 min", Units.Duration(new TimeSpan(3, 0, 0, 0)));
    }

    [Theory]
    [InlineData(1_000_000_000L, "1 Gbps")]
    [InlineData(866_700_000L, "866.7 Mbps")]
    public void Link_speed(long bps, string expected) => Assert.Equal(expected, Units.LinkSpeed(bps));

    [Theory]
    [InlineData("Windows 10 Pro", 26200, "Windows 11 Pro")]
    [InlineData("Windows 10 Pro", 19045, "Windows 10 Pro")]
    [InlineData("Windows 10 Enterprise LTSC 2021", 19044, "Windows 10 Enterprise LTSC 2021")]
    [InlineData("Windows Server 2022 Standard", 20348, "Windows Server 2022 Standard")]
    public void Windows_11_is_named_correctly(string product, int build, string expected) =>
        Assert.Equal(expected, SystemCollector.WindowsName(product, build));
}

public class MemoryTests
{
    [Theory]
    [InlineData(26, 0, "DDR4")]
    [InlineData(34, 0, "DDR5")]
    [InlineData(35, 0, "LPDDR5")]
    [InlineData(0, 24, "DDR3")]
    [InlineData(0, 0, null)]
    public void Types(long smbios, long legacy, string? expected) => Assert.Equal(expected, MemoryCollector.TypeName(smbios, legacy));

    [Fact]
    public void Speed_shows_dram_clock() => Assert.Equal("3200 MT/s (DRAM clock 1600 MHz)", MemoryCollector.Speed(3200));

    [Theory]
    [InlineData("80CE000080CE", null, "Samsung")]
    [InlineData("Kingston", null, "Kingston")]
    [InlineData(null, "CMT16GX4M2C3200C16", "Corsair (from part number)")]
    [InlineData(null, "F5-6000J3038F16G", "G.Skill (from part number)")]
    [InlineData("859B", "BL16G32C16U4B.M8FE1", "Crucial")]
    [InlineData(null, "XYZ123", null)]
    [InlineData("ABCD1234", null, null)]
    public void Manufacturers(string? reported, string? part, string? expected) =>
        Assert.Equal(expected, MemoryCollector.ManufacturerName(reported, part));

    [Fact]
    public void Channels_from_slot_labels()
    {
        Assert.Equal(2, MemoryCollector.EstimateChannels(["P0 CHANNEL A DIMM 0", "P0 CHANNEL A DIMM 1", "P0 CHANNEL B DIMM 0", "P0 CHANNEL B DIMM 1"]));
        Assert.Equal(2, MemoryCollector.EstimateChannels(["BANK 0 DIMM_A1", "BANK 1 DIMM_B1"]));
        Assert.Equal(0, MemoryCollector.EstimateChannels(["BANK 0", "BANK 1"]));
    }
}

public class GraphicsTests
{
    [Theory]
    [InlineData("NVIDIA GeForce RTX 3080", "32.0.16.1742", "NVIDIA 617.42")]
    [InlineData("NVIDIA GeForce RTX 4090", "32.0.15.6174", "NVIDIA 561.74")]
    [InlineData("NVIDIA GeForce GTX 1060", "31.0.15.0612", "NVIDIA 506.12")]
    [InlineData("AMD Radeon RX 7800 XT", "32.0.12011.1036", null)]
    public void Nvidia_driver_versions(string name, string version, string? expected) =>
        Assert.Equal(expected, GraphicsCollector.NvidiaDriverVersion(name, version));

    [Fact]
    public void Pci_ids() => Assert.Equal("10DE:2216, subsystem 1458:403E, rev A1",
        GraphicsCollector.PciIds(@"PCI\VEN_10DE&DEV_2216&SUBSYS_403E1458&REV_A1\4&2283F625&0&0019"));
}
