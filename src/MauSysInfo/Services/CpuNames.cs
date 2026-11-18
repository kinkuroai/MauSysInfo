namespace MauSysInfo.Services;

public sealed record CpuCodename(string Codename, string? Microarchitecture, string? Process);

/// <summary>
/// Maps a CPU's family/model to its codename, for the desktop and laptop generations people actually run.
/// Unknown signatures return null and the card just leaves the line out (never a guess).
/// </summary>
public static class CpuNames
{
    private static readonly Dictionary<(int Family, int Model), CpuCodename> Intel = new()
    {
        [(6, 0x2A)] = new("Sandy Bridge", "Sandy Bridge", "32 nm"),
        [(6, 0x2D)] = new("Sandy Bridge-E", "Sandy Bridge", "32 nm"),
        [(6, 0x3A)] = new("Ivy Bridge", "Ivy Bridge", "22 nm"),
        [(6, 0x3E)] = new("Ivy Bridge-E", "Ivy Bridge", "22 nm"),
        [(6, 0x3C)] = new("Haswell", "Haswell", "22 nm"),
        [(6, 0x45)] = new("Haswell-ULT", "Haswell", "22 nm"),
        [(6, 0x46)] = new("Crystal Well", "Haswell", "22 nm"),
        [(6, 0x3F)] = new("Haswell-E", "Haswell", "22 nm"),
        [(6, 0x3D)] = new("Broadwell", "Broadwell", "14 nm"),
        [(6, 0x47)] = new("Broadwell-H", "Broadwell", "14 nm"),
        [(6, 0x4F)] = new("Broadwell-E", "Broadwell", "14 nm"),
        [(6, 0x4E)] = new("Skylake-U/Y", "Skylake", "14 nm"),
        [(6, 0x5E)] = new("Skylake-S", "Skylake", "14 nm"),
        [(6, 0x55)] = new("Skylake-X / Cascade Lake", "Skylake", "14 nm"),
        [(6, 0x66)] = new("Cannon Lake", "Palm Cove", "10 nm"),
        [(6, 0x7D)] = new("Ice Lake", "Sunny Cove", "10 nm"),
        [(6, 0x7E)] = new("Ice Lake-U/Y", "Sunny Cove", "10 nm"),
        [(6, 0x6A)] = new("Ice Lake-SP", "Sunny Cove", "10 nm"),
        [(6, 0xA5)] = new("Comet Lake-S", "Skylake", "14 nm"),
        [(6, 0xA6)] = new("Comet Lake-U", "Skylake", "14 nm"),
        [(6, 0xA7)] = new("Rocket Lake", "Cypress Cove", "14 nm"),
        [(6, 0x8C)] = new("Tiger Lake-U", "Willow Cove", "10 nm SuperFin"),
        [(6, 0x8D)] = new("Tiger Lake-H", "Willow Cove", "10 nm SuperFin"),
        [(6, 0x97)] = new("Alder Lake-S", "Golden Cove + Gracemont", "Intel 7"),
        [(6, 0x9A)] = new("Alder Lake-P", "Golden Cove + Gracemont", "Intel 7"),
        [(6, 0xBE)] = new("Alder Lake-N", "Gracemont", "Intel 7"),
        [(6, 0xB7)] = new("Raptor Lake-S", "Raptor Cove + Gracemont", "Intel 7"),
        [(6, 0xBA)] = new("Raptor Lake-P", "Raptor Cove + Gracemont", "Intel 7"),
        [(6, 0xBF)] = new("Raptor Lake-S", null, "Intel 7"),
        [(6, 0x8F)] = new("Sapphire Rapids", "Golden Cove", "Intel 7"),
        [(6, 0xCF)] = new("Emerald Rapids", "Raptor Cove", "Intel 7"),
        [(6, 0xAA)] = new("Meteor Lake", "Redwood Cove + Crestmont", "Intel 4"),
        [(6, 0xAC)] = new("Meteor Lake", "Redwood Cove + Crestmont", "Intel 4"),
        [(6, 0xBD)] = new("Lunar Lake", "Lion Cove + Skymont", "TSMC N3B"),
        [(6, 0xC6)] = new("Arrow Lake-S", "Lion Cove + Skymont", "TSMC N3B"),
        [(6, 0xC5)] = new("Arrow Lake-H", "Lion Cove + Skymont", "TSMC N3B"),
        [(6, 0xB5)] = new("Arrow Lake-U", "Redwood Cove + Crestmont", "Intel 3"),
        [(6, 0xCC)] = new("Panther Lake", "Cougar Cove + Darkmont", "Intel 18A"),
    };

    private static readonly Dictionary<(int Family, int Model), CpuCodename> Amd = new()
    {
        [(0x17, 0x01)] = new("Summit Ridge", "Zen", "14 nm"),
        [(0x17, 0x08)] = new("Pinnacle Ridge", "Zen+", "12 nm"),
        [(0x17, 0x11)] = new("Raven Ridge", "Zen", "14 nm"),
        [(0x17, 0x18)] = new("Picasso", "Zen+", "12 nm"),
        [(0x17, 0x20)] = new("Dali", "Zen", "14 nm"),
        [(0x17, 0x31)] = new("Rome / Castle Peak", "Zen 2", "7 nm"),
        [(0x17, 0x60)] = new("Renoir", "Zen 2", "7 nm"),
        [(0x17, 0x68)] = new("Lucienne", "Zen 2", "7 nm"),
        [(0x17, 0x71)] = new("Matisse", "Zen 2", "7 nm"),
        [(0x17, 0x90)] = new("Van Gogh", "Zen 2", "7 nm"),
        [(0x17, 0xA0)] = new("Mendocino", "Zen 2", "6 nm"),
        [(0x19, 0x01)] = new("Milan", "Zen 3", "7 nm"),
        [(0x19, 0x08)] = new("Chagall", "Zen 3", "7 nm"),
        [(0x19, 0x21)] = new("Vermeer", "Zen 3", "7 nm"),
        [(0x19, 0x44)] = new("Rembrandt", "Zen 3+", "6 nm"),
        [(0x19, 0x50)] = new("Cezanne", "Zen 3", "7 nm"),
        [(0x19, 0x11)] = new("Genoa", "Zen 4", "5 nm"),
        [(0x19, 0x18)] = new("Storm Peak", "Zen 4", "5 nm"),
        [(0x19, 0x61)] = new("Raphael", "Zen 4", "5 nm"),
        [(0x19, 0x74)] = new("Phoenix", "Zen 4", "4 nm"),
        [(0x19, 0x75)] = new("Phoenix / Hawk Point", "Zen 4", "4 nm"),
        [(0x19, 0x78)] = new("Phoenix 2", "Zen 4 + Zen 4c", "4 nm"),
        [(0x1A, 0x44)] = new("Granite Ridge", "Zen 5", "4 nm"),
        [(0x1A, 0x24)] = new("Strix Point", "Zen 5 + Zen 5c", "4 nm"),
        [(0x1A, 0x60)] = new("Krackan Point", "Zen 5 + Zen 5c", "4 nm"),
        [(0x1A, 0x70)] = new("Strix Halo", "Zen 5", "4 nm"),
    };

    public static CpuCodename? Lookup(string vendor, int family, int model, int stepping = 0)
    {
        if (vendor == "GenuineIntel")
        {
            // Kaby Lake, Coffee Lake and their successors share one model number and differ by stepping.
            if (family == 6 && model == 0x9E)
                return stepping <= 9 ? new("Kaby Lake", "Skylake", "14 nm") : new("Coffee Lake", "Skylake", "14 nm");
            if (family == 6 && model == 0x8E)
                return stepping switch
                {
                    <= 9 => new("Kaby Lake-U/Y", "Skylake", "14 nm"),
                    10 => new("Kaby Lake-R", "Skylake", "14 nm"),
                    11 or 12 => new("Whiskey Lake / Comet Lake-U", "Skylake", "14 nm"),
                    _ => new("Kaby Lake (mobile)", "Skylake", "14 nm"),
                };
            return Intel.GetValueOrDefault((family, model));
        }
        if (vendor is "AuthenticAMD" or "HygonGenuine")
            return Amd.GetValueOrDefault((family, model));
        return null;
    }

    /// <summary>"Intel", "AMD" or the raw vendor string.</summary>
    public static string VendorName(string vendor) => vendor switch
    {
        "GenuineIntel" => "Intel",
        "AuthenticAMD" => "AMD",
        "HygonGenuine" => "Hygon",
        "CentaurHauls" => "Centaur",
        "  Shanghai  " or "Shanghai" => "Zhaoxin",
        _ => vendor,
    };
}
