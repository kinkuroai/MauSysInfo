using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;

namespace MauSysInfo.Services;

public readonly record struct LogicalCpu(int Group, int Number)
{
    /// <summary>Instance name in the "Processor Information" performance counters: "0,5".</summary>
    public string CounterInstance => $"{Group},{Number}";
}

public sealed record CpuCore(int Index, byte EfficiencyClass, bool Smt, IReadOnlyList<LogicalCpu> Threads);

public enum CacheKind { Unified = 0, Instruction = 1, Data = 2, Trace = 3 }

public sealed record CacheInstance(int Level, CacheKind Kind, long Size, int Associativity, int LineSize, int SharedBy);

/// <summary>Summary of identical caches: "12 x 32 KB, 8-way".</summary>
public sealed record CacheSummary(int Level, CacheKind Kind, long Size, int Associativity, int LineSize, int Count, int SharedBy)
{
    public string Name => Kind switch
    {
        CacheKind.Data => $"L{Level} Data",
        CacheKind.Instruction => $"L{Level} Instruction",
        CacheKind.Trace => $"L{Level} Trace",
        _ => $"L{Level}",
    };

    public string Describe() =>
        $"{(Count > 1 ? $"{Count} x " : "")}{Units.Bytes(Size)}" +
        (Associativity == 0xFF ? ", fully associative" : Associativity > 0 ? $", {Associativity}-way" : "") +
        (LineSize > 0 ? $", {LineSize}-byte line" : "");
}

/// <summary>Cores, threads, packages and caches from GetLogicalProcessorInformationEx.</summary>
public sealed class CpuTopology
{
    public List<CpuCore> Cores { get; } = [];
    public List<CacheInstance> Caches { get; } = [];
    public int Packages { get; set; }
    public int NumaNodes { get; set; }

    public int ThreadCount => Cores.Sum(c => c.Threads.Count);

    /// <summary>True on Intel 12th gen+ style CPUs that mix performance and efficiency cores.</summary>
    public bool IsHybrid => Cores.Select(c => c.EfficiencyClass).Distinct().Count() > 1;

    /// <summary>Windows gives the faster cores the higher efficiency class.</summary>
    public bool IsPerformanceCore(CpuCore core) => core.EfficiencyClass == Cores.Max(c => c.EfficiencyClass);

    public int PerformanceCores => IsHybrid ? Cores.Count(IsPerformanceCore) : Cores.Count;
    public int EfficiencyCores => IsHybrid ? Cores.Count(c => !IsPerformanceCore(c)) : 0;

    public IReadOnlyList<CacheSummary> CacheSummaries() =>
        Caches.GroupBy(c => (c.Level, c.Kind, c.Size, c.Associativity, c.LineSize))
              .Select(g => new CacheSummary(g.Key.Level, g.Key.Kind, g.Key.Size, g.Key.Associativity, g.Key.LineSize,
                                            g.Count(), g.Max(c => c.SharedBy)))
              .OrderBy(s => s.Level)
              .ThenBy(s => s.Kind == CacheKind.Data ? 0 : s.Kind == CacheKind.Instruction ? 1 : 2)
              .ThenByDescending(s => s.Size)
              .ToList();

    public static CpuTopology? ReadLocal()
    {
        const int RelationAll = 0xFFFF;
        uint length = 0;
        Native.GetLogicalProcessorInformationEx(RelationAll, IntPtr.Zero, ref length);
        if (length == 0) return null;

        var buffer = Marshal.AllocHGlobal((int)length);
        try
        {
            if (!Native.GetLogicalProcessorInformationEx(RelationAll, buffer, ref length)) return null;
            var bytes = new byte[length];
            Marshal.Copy(buffer, bytes, 0, (int)length);
            return Parse(bytes);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Parses the SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX records (64-bit layout).</summary>
    public static CpuTopology Parse(ReadOnlySpan<byte> data)
    {
        var topology = new CpuTopology();
        int offset = 0;
        while (offset + 8 <= data.Length)
        {
            int relationship = BinaryPrimitives.ReadInt32LittleEndian(data[offset..]);
            int size = BinaryPrimitives.ReadInt32LittleEndian(data[(offset + 4)..]);
            if (size <= 0 || offset + size > data.Length) break;
            var body = data.Slice(offset + 8, size - 8);

            switch (relationship)
            {
                case 0: // RelationProcessorCore
                {
                    byte flags = body[0];
                    byte efficiency = body[1];
                    int groupCount = BinaryPrimitives.ReadUInt16LittleEndian(body[22..]);
                    var threads = ReadGroupMasks(body[24..], groupCount);
                    topology.Cores.Add(new CpuCore(topology.Cores.Count, efficiency, (flags & 1) != 0, threads));
                    break;
                }
                case 1: // RelationNumaNode
                    topology.NumaNodes++;
                    break;
                case 2: // RelationCache
                {
                    int level = body[0];
                    int assoc = body[1];
                    int line = BinaryPrimitives.ReadUInt16LittleEndian(body[2..]);
                    long cacheSize = BinaryPrimitives.ReadUInt32LittleEndian(body[4..]);
                    var kind = (CacheKind)BinaryPrimitives.ReadInt32LittleEndian(body[8..]);
                    int groupCount = BinaryPrimitives.ReadUInt16LittleEndian(body[30..]);
                    // Before Windows 11 GroupCount was reserved (0) and exactly one mask followed.
                    var shared = ReadGroupMasks(body[32..], Math.Max(1, groupCount));
                    topology.Caches.Add(new CacheInstance(level, kind, cacheSize, assoc, line, shared.Count));
                    break;
                }
                case 3: // RelationProcessorPackage
                    topology.Packages++;
                    break;
            }
            offset += size;
        }
        return topology;
    }

    /// <summary>GROUP_AFFINITY array: 8-byte mask, 2-byte group, 6 reserved bytes each.</summary>
    private static List<LogicalCpu> ReadGroupMasks(ReadOnlySpan<byte> data, int count)
    {
        var cpus = new List<LogicalCpu>();
        for (int i = 0; i < count && (i + 1) * 16 <= data.Length; i++)
        {
            ulong mask = BinaryPrimitives.ReadUInt64LittleEndian(data[(i * 16)..]);
            int group = BinaryPrimitives.ReadUInt16LittleEndian(data[(i * 16 + 8)..]);
            while (mask != 0)
            {
                int bit = BitOperations.TrailingZeroCount(mask);
                cpus.Add(new LogicalCpu(group, bit));
                mask &= mask - 1;
            }
        }
        return cpus;
    }
}
