using System.Runtime.Intrinsics.X86;
using System.Text;

namespace MauSysInfo.Services;

public readonly record struct CpuidRegs(uint Eax, uint Ebx, uint Ecx, uint Edx);

/// <summary>Identity read straight from the CPUID instruction (x86/x64 only).</summary>
public sealed record CpuIdentity(
    string Vendor,
    string Brand,
    int Family,
    int Model,
    int Stepping,
    int BaseFamily,
    int BaseModel,
    int ExtFamily,
    int ExtModel,
    IReadOnlyList<string> InstructionSets,
    bool HypervisorPresent,
    bool HardwareVirtualization,
    uint SignatureEax)
{
    /// <summary>The raw leaf-1 signature as hex, the form microcode and errata documents use: "A20F12".</summary>
    public string SignatureHex => SignatureEax.ToString("X");

    public bool IsIntel => Vendor == "GenuineIntel";
    public bool IsAmd => Vendor == "AuthenticAMD";
}

/// <summary>Decodes CPUID. The decoding is pure so it can be tested with captured register values.</summary>
public static class Cpuid
{
    /// <summary>Reads this machine's CPUID, or null on ARM64 where the instruction doesn't exist.</summary>
    public static CpuIdentity? ReadLocal()
    {
        if (!X86Base.IsSupported) return null;
        try { return Decode(Query); }
        catch { return null; }
    }

    private static CpuidRegs Query(uint leaf, uint subleaf)
    {
        var (a, b, c, d) = X86Base.CpuId(unchecked((int)leaf), unchecked((int)subleaf));
        return new CpuidRegs((uint)a, (uint)b, (uint)c, (uint)d);
    }

    /// <summary>Decodes from any CPUID source (the real instruction, or a table in tests).</summary>
    public static CpuIdentity Decode(Func<uint, uint, CpuidRegs> cpuid)
    {
        var leaf0 = cpuid(0, 0);
        uint maxLeaf = leaf0.Eax;
        string vendor = Ascii(leaf0.Ebx, leaf0.Edx, leaf0.Ecx);

        var leaf1 = maxLeaf >= 1 ? cpuid(1, 0) : default;
        var (family, model, stepping, baseFamily, baseModel, extFamily, extModel) = Signature(leaf1.Eax);

        var leaf7 = maxLeaf >= 7 ? cpuid(7, 0) : default;
        var leaf7s1 = maxLeaf >= 7 && leaf7.Eax >= 1 ? cpuid(7, 1) : default;

        uint maxExt = cpuid(0x80000000, 0).Eax;
        var ext1 = maxExt >= 0x80000001 ? cpuid(0x80000001, 0) : default;

        string brand = "";
        if (maxExt >= 0x80000004)
        {
            var sb = new StringBuilder();
            for (uint leaf = 0x80000002; leaf <= 0x80000004; leaf++)
            {
                var r = cpuid(leaf, 0);
                sb.Append(Ascii(r.Eax, r.Ebx, r.Ecx, r.Edx));
            }
            brand = string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        var sets = InstructionSets(leaf1, leaf7, leaf7s1, ext1, vendor);
        bool hypervisor = Bit(leaf1.Ecx, 31);
        bool hwVirt = Bit(leaf1.Ecx, 5) || Bit(ext1.Ecx, 2);

        return new CpuIdentity(vendor, brand, family, model, stepping, baseFamily, baseModel, extFamily, extModel,
            sets, hypervisor, hwVirt, leaf1.Eax);
    }

    /// <summary>Family/model/stepping from leaf 1 EAX, with the extended fields folded in as Intel and AMD document.</summary>
    public static (int Family, int Model, int Stepping, int BaseFamily, int BaseModel, int ExtFamily, int ExtModel) Signature(uint eax)
    {
        int stepping = (int)(eax & 0xF);
        int baseModel = (int)((eax >> 4) & 0xF);
        int baseFamily = (int)((eax >> 8) & 0xF);
        int extModel = (int)((eax >> 16) & 0xF);
        int extFamily = (int)((eax >> 20) & 0xFF);

        int family = baseFamily == 0xF ? baseFamily + extFamily : baseFamily;
        int model = baseFamily is 0x6 or 0xF ? (extModel << 4) | baseModel : baseModel;
        return (family, model, stepping, baseFamily, baseModel, extFamily, extModel);
    }

    public static IReadOnlyList<string> InstructionSets(CpuidRegs leaf1, CpuidRegs leaf7, CpuidRegs leaf7s1, CpuidRegs ext1, string vendor)
    {
        var list = new List<string>();
        void Add(bool present, string name) { if (present) list.Add(name); }

        Add(Bit(leaf1.Edx, 23), "MMX");
        Add(Bit(ext1.Edx, 31) && vendor == "AuthenticAMD", "3DNow!");
        Add(Bit(leaf1.Edx, 25), "SSE");
        Add(Bit(leaf1.Edx, 26), "SSE2");
        Add(Bit(leaf1.Ecx, 0), "SSE3");
        Add(Bit(leaf1.Ecx, 9), "SSSE3");
        Add(Bit(leaf1.Ecx, 19), "SSE4.1");
        Add(Bit(leaf1.Ecx, 20), "SSE4.2");
        Add(Bit(ext1.Ecx, 6), "SSE4A");
        Add(Bit(ext1.Edx, 29), "x86-64");
        Add(Bit(leaf1.Ecx, 5), "VT-x");
        Add(Bit(ext1.Ecx, 2), "AMD-V");
        Add(Bit(leaf1.Ecx, 25), "AES");
        Add(Bit(leaf1.Ecx, 28), "AVX");
        Add(Bit(leaf7.Ebx, 5), "AVX2");
        Add(Bit(leaf7s1.Eax, 4), "AVX-VNNI");
        Add(Bit(leaf7.Ebx, 16), "AVX-512F");
        Add(Bit(leaf7.Ebx, 17), "AVX-512DQ");
        Add(Bit(leaf7.Ebx, 28), "AVX-512CD");
        Add(Bit(leaf7.Ebx, 30), "AVX-512BW");
        Add(Bit(leaf7.Ebx, 31), "AVX-512VL");
        Add(Bit(leaf7.Ecx, 11), "AVX-512 VNNI");
        Add(Bit(leaf7s1.Eax, 5), "AVX-512 BF16");
        Add(Bit(leaf7.Edx, 23), "AVX-512 FP16");
        Add(Bit(leaf1.Ecx, 12), "FMA3");
        Add(Bit(ext1.Ecx, 16), "FMA4");
        Add(Bit(leaf7.Ebx, 29), "SHA");
        Add(Bit(leaf7.Ecx, 9), "VAES");
        Add(Bit(leaf1.Ecx, 1), "PCLMULQDQ");
        Add(Bit(leaf7.Ecx, 8), "GFNI");
        Add(Bit(leaf7.Ebx, 3), "BMI1");
        Add(Bit(leaf7.Ebx, 8), "BMI2");
        Add(Bit(leaf7.Ebx, 19), "ADX");
        Add(Bit(leaf1.Ecx, 29), "F16C");
        Add(Bit(leaf1.Ecx, 23), "POPCNT");
        Add(Bit(ext1.Ecx, 5), "LZCNT");
        Add(Bit(leaf1.Ecx, 30), "RDRAND");
        Add(Bit(leaf7.Ebx, 18), "RDSEED");
        Add(Bit(leaf7.Edx, 24), "AMX-TILE");
        Add(Bit(leaf7.Edx, 22), "AMX-BF16");
        Add(Bit(leaf7.Edx, 25), "AMX-INT8");
        return list;
    }

    private static bool Bit(uint value, int bit) => (value & (1u << bit)) != 0;

    private static string Ascii(params uint[] regs)
    {
        var bytes = new byte[regs.Length * 4];
        for (int i = 0; i < regs.Length; i++) BitConverter.GetBytes(regs[i]).CopyTo(bytes, i * 4);
        return Encoding.ASCII.GetString(bytes).TrimEnd('\0').Trim();
    }
}
