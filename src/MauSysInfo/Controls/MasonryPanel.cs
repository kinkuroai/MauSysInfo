using System.Windows;
using System.Windows.Controls;

namespace MauSysInfo.Controls;

/// <summary>
/// Lays cards out in as many columns as fit (at least <see cref="MinColumnWidth"/> each), putting every card
/// into the currently shortest column so cards of different heights don't leave gaps.
/// </summary>
public sealed class MasonryPanel : Panel
{
    public static readonly DependencyProperty MinColumnWidthProperty = DependencyProperty.Register(
        nameof(MinColumnWidth), typeof(double), typeof(MasonryPanel),
        new FrameworkPropertyMetadata(380.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(MasonryPanel),
        new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double MinColumnWidth
    {
        get => (double)GetValue(MinColumnWidthProperty);
        set => SetValue(MinColumnWidthProperty, value);
    }

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    private (int Columns, double Width) Layout(double available)
    {
        if (double.IsInfinity(available) || available <= 0) available = MinColumnWidth;
        int columns = Math.Max(1, (int)((available + Spacing) / (MinColumnWidth + Spacing)));
        columns = Math.Min(columns, Math.Max(1, InternalChildren.Count));
        double width = (available - Spacing * (columns - 1)) / columns;
        return (columns, Math.Max(0, width));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var (columns, width) = Layout(availableSize.Width);
        var heights = new double[columns];
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(width, double.PositiveInfinity));
            int col = Array.IndexOf(heights, heights.Min());
            heights[col] += child.DesiredSize.Height + Spacing;
        }
        double total = InternalChildren.Count == 0 ? 0 : Math.Max(0, heights.Max() - Spacing);
        double w = double.IsInfinity(availableSize.Width) ? width * columns + Spacing * (columns - 1) : availableSize.Width;
        return new Size(w, total);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (columns, width) = Layout(finalSize.Width);
        var heights = new double[columns];
        foreach (UIElement child in InternalChildren)
        {
            int col = Array.IndexOf(heights, heights.Min());
            child.Arrange(new Rect(col * (width + Spacing), heights[col], width, child.DesiredSize.Height));
            heights[col] += child.DesiredSize.Height + Spacing;
        }
        return finalSize;
    }
}
