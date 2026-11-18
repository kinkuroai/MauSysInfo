using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MauSysInfo.Models;
using MauSysInfo.Services;

namespace MauSysInfo.ViewModels;

/// <summary>One entry in the side navigation and the cards it shows.</summary>
public sealed partial class PageViewModel : ObservableObject
{
    private readonly Func<PageData>? _collect;

    public PageViewModel(string title, string glyph, Func<PageData>? collect)
    {
        Title = title;
        Glyph = glyph;
        _collect = collect;
    }

    public string Title { get; }

    /// <summary>Segoe Fluent Icons glyph.</summary>
    public string Glyph { get; }

    public ObservableCollection<InfoBlock> WideBlocks { get; } = [];
    public ObservableCollection<InfoBlock> ColumnBlocks { get; } = [];

    public IEnumerable<InfoBlock> AllBlocks => WideBlocks.Concat(ColumnBlocks).Concat(TrailingBlocks);

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _error;

    /// <summary>Collects on a worker thread (WMI is slow) and swaps the cards in on the UI thread.</summary>
    public async Task<PageData?> LoadAsync()
    {
        if (_collect is null) return null;
        IsLoading = true;
        Error = null;
        try
        {
            // WMI from an STA UI thread is slow and can deadlock; collect on the thread pool.
            var data = await Task.Run(_collect);
            SetBlocks(data.Blocks);
            if (data.Blocks.Count == 0) Error = "Windows didn't report anything for this page.";
            return data;
        }
        catch (Exception ex)
        {
            Error = $"Couldn't read this page: {ex.Message}";
            return null;
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void SetBlocks(IEnumerable<InfoBlock> blocks)
    {
        WideBlocks.Clear();
        ColumnBlocks.Clear();
        // Wide cards that come first stay on top; the rest flow in columns, with any later wide card at the bottom.
        bool leading = true;
        var trailing = new List<InfoBlock>();
        foreach (var block in blocks)
        {
            if (block.Wide && leading) WideBlocks.Add(block);
            else if (block.Wide) trailing.Add(block);
            else { leading = false; ColumnBlocks.Add(block); }
        }
        TrailingBlocks.Clear();
        foreach (var block in trailing) TrailingBlocks.Add(block);
        OnPropertyChanged(nameof(AllBlocks));
    }

    /// <summary>Wide cards placed after the columns (the per-thread grid on the CPU page).</summary>
    public ObservableCollection<InfoBlock> TrailingBlocks { get; } = [];
}
