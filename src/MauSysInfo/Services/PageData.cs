using MauSysInfo.Models;

namespace MauSysInfo.Services;

/// <summary>What a collector produced for one page, plus the fields the live monitor keeps updating.</summary>
public sealed class PageData
{
    public List<InfoBlock> Blocks { get; } = [];

    /// <summary>Fields the live monitor updates, by key (see <see cref="LiveKeys"/>).</summary>
    public Dictionary<string, InfoField> Live { get; } = [];

    /// <summary>Per-thread tiles with the logical processor each one shows.</summary>
    public List<(ThreadTile Tile, LogicalCpu Cpu)> Threads { get; } = [];

    public FieldsBlock AddBlock(string title, string? subtitle = null, bool wide = false)
    {
        var block = new FieldsBlock(title) { Subtitle = subtitle, Wide = wide };
        Blocks.Add(block);
        return block;
    }

    /// <summary>Drops cards a collector started but found nothing for.</summary>
    public PageData WithoutEmptyBlocks()
    {
        Blocks.RemoveAll(b => b is FieldsBlock { Fields.Count: 0 });
        return this;
    }
}

public static class LiveKeys
{
    public const string CpuClock = "cpu.clock";
    public const string CpuUsage = "cpu.usage";
    public const string MemoryInUse = "mem.inuse";
    public const string MemoryAvailable = "mem.available";
    public const string MemoryCommitted = "mem.committed";
    public const string Uptime = "sys.uptime";
}
