using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MauSysInfo.Models;

/// <summary>One "Label: value" line. Value and Meter can change while the app runs (live clocks, usage).</summary>
public sealed partial class InfoField : ObservableObject
{
    public InfoField(string label, string value, string? note = null, double? meter = null)
    {
        Label = label;
        _value = value;
        Note = note;
        _meter = meter;
    }

    public string Label { get; }

    /// <summary>Extra explanation, shown as a tooltip.</summary>
    public string? Note { get; }

    [ObservableProperty]
    private string _value;

    /// <summary>0..1 to draw a bar under the value (usage, used space), or null for none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMeter))]
    private double? _meter;

    public bool HasMeter => Meter is not null;
}

/// <summary>A card on a page.</summary>
public abstract class InfoBlock
{
    protected InfoBlock(string title) => Title = title;
    public string Title { get; }

    /// <summary>Optional line under the title (a model name, a drive letter).</summary>
    public string? Subtitle { get; init; }

    /// <summary>Wide cards span the whole page instead of sitting in a column.</summary>
    public bool Wide { get; init; }
}

/// <summary>A card of label/value lines.</summary>
public sealed class FieldsBlock : InfoBlock
{
    public FieldsBlock(string title, IEnumerable<InfoField>? fields = null) : base(title)
    {
        Fields = new ObservableCollection<InfoField>(fields ?? []);
    }

    public ObservableCollection<InfoField> Fields { get; }

    /// <summary>Adds a line unless the value is empty, so cards only show what the hardware reports.</summary>
    public InfoField? Add(string label, string? value, string? note = null, double? meter = null)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var field = new InfoField(label, value.Trim(), note, meter);
        Fields.Add(field);
        return field;
    }

    /// <summary>Adds a line even when the value is empty (shown as a dash). For values filled in live.</summary>
    public InfoField AddLive(string label, string? note = null)
    {
        var field = new InfoField(label, "—", note);
        Fields.Add(field);
        return field;
    }
}

/// <summary>A small live tile for one logical processor.</summary>
public sealed partial class ThreadTile : ObservableObject
{
    public ThreadTile(int index, string coreLabel, string? kind)
    {
        Index = index;
        CoreLabel = coreLabel;
        Kind = kind;
    }

    public int Index { get; }
    public string Title => $"CPU {Index}";
    public string CoreLabel { get; }

    /// <summary>"P" / "E" on hybrid CPUs, null otherwise.</summary>
    public string? Kind { get; }
    public bool HasKind => Kind is not null;

    [ObservableProperty] private string _clock = "—";
    [ObservableProperty] private double _usage;
    [ObservableProperty] private string _usageText = "—";
}

/// <summary>Per-thread clock and usage, updated every second.</summary>
public sealed class ThreadsBlock : InfoBlock
{
    public ThreadsBlock(string title, IEnumerable<ThreadTile> threads) : base(title)
    {
        Threads = new ObservableCollection<ThreadTile>(threads);
    }

    public ObservableCollection<ThreadTile> Threads { get; }
}

/// <summary>Marker block the page template replaces with the benchmark panel.</summary>
public sealed class BenchBlock : InfoBlock
{
    public BenchBlock(object viewModel) : base("Benchmark") => ViewModel = viewModel;
    public object ViewModel { get; }
}
