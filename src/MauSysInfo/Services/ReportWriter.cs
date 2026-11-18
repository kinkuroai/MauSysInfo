using System.Text;
using MauSysInfo.Models;

namespace MauSysInfo.Services;

/// <summary>Plain-text report of every page, for saving or pasting into a forum post / support ticket.</summary>
public static class ReportWriter
{
    public static string Build(IEnumerable<(string Title, IEnumerable<InfoBlock> Blocks)> pages, DateTime when)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{AppInfo.Name} {AppInfo.Version} report");
        sb.AppendLine($"Created {when:yyyy-MM-dd HH:mm}");
        sb.AppendLine();

        foreach (var (title, blocks) in pages)
        {
            var fieldBlocks = blocks.OfType<FieldsBlock>().Where(b => b.Fields.Count > 0).ToList();
            if (fieldBlocks.Count == 0) continue;

            sb.AppendLine(new string('=', 60));
            sb.AppendLine(title.ToUpperInvariant());
            sb.AppendLine(new string('=', 60));
            foreach (var block in fieldBlocks)
            {
                sb.AppendLine();
                sb.AppendLine(block.Subtitle is { } sub && sub != block.Title ? $"{block.Title} — {sub}" : block.Title);
                sb.AppendLine(new string('-', Math.Min(60, Math.Max(block.Title.Length, 8))));
                int width = Math.Min(28, block.Fields.Max(f => f.Label.Length)) + 2;
                foreach (var field in block.Fields)
                    AppendField(sb, field.Label, field.Value, width);
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>"Label ........ value", wrapping long values under the value column.</summary>
    internal static void AppendField(StringBuilder sb, string label, string value, int width)
    {
        string prefix = (label + " ").PadRight(width, '.') + " ";
        string indent = new(' ', prefix.Length);
        const int lineWidth = 100;
        var words = value.Split(' ');
        var line = new StringBuilder(prefix);
        bool first = true;
        foreach (var word in words)
        {
            if (!first && line.Length + 1 + word.Length > lineWidth)
            {
                sb.AppendLine(line.ToString());
                line.Clear().Append(indent).Append(word);
            }
            else
            {
                if (!first) line.Append(' ');
                line.Append(word);
            }
            first = false;
        }
        sb.AppendLine(line.ToString());
    }
}
