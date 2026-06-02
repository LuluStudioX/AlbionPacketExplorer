using Avalonia.Controls;
using AlbionPacketExplorer.Controls;
using AlbionPacketExplorer.ViewModels;
using AlbionPacketExplorer.Services;
using System.Text;

namespace AlbionPacketExplorer.Views;

public partial class ExpandedValueWindow : ApxWindow
{
    public ExpandedValueWindow(ParamRow row, GameDataService gameData)
    {
        InitializeComponent();
        var text = BuildText(row, gameData);
        var box = this.FindControl<TextBox>("ValueBox");
        if (box != null)
            box.Text = text;

        Opened += (_, _) => SizeForText(text);
    }

    // Pick a window size from the content: width by the longest line, height by line
    // count, both clamped to the screen by the ApxWindow helper.
    private void SizeForText(string text)
    {
        var lines = text.Split('\n');
        var longest = 0;
        foreach (var line in lines)
            if (line.Length > longest) longest = line.Length;

        const double charWidth = 7.5;   // monospace approximation
        const double lineHeight = 17.0;
        const double padding = 48.0;

        var desiredWidth = longest * charWidth + padding;
        var desiredHeight = lines.Length * lineHeight + padding;
        SizeToScreen(desiredWidth, desiredHeight, minWidth: 360, minHeight: 200);
    }

    private static string BuildText(ParamRow row, GameDataService gameData)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Key:  {row.Key}{(string.IsNullOrEmpty(row.SchemaName) ? "" : $"  ({row.SchemaName})")}");
        sb.AppendLine($"Type: {row.Type}");
        sb.AppendLine();

        var pv = row.Value;

        // Already-resolved items
        if (row.HasResolvedItems)
        {
            sb.AppendLine("Resolved items:");
            foreach (var ri in row.ResolvedItems)
                sb.AppendLine($"  {ri.UniqueName} — {ri.DisplayName}");
            sb.AppendLine();
        }
        else if (row.HasSingleResolved)
        {
            sb.AppendLine($"Resolved: {row.ResolvedName}");
            sb.AppendLine();
        }

        // Preview resolve for non-resolved rows
        if (!row.HasResolved && gameData.IsLoaded && !string.IsNullOrEmpty(row.PreviewText))
        {
            sb.AppendLine("Preview resolve:");
            foreach (var line in row.PreviewText.Split('\n'))
                sb.AppendLine($"  {line}");
            sb.AppendLine();
        }

        sb.AppendLine("Raw value:");
        // Pretty-print arrays one per line
        var raw = row.Value;
        if (raw.StartsWith('[') && raw.EndsWith(']'))
        {
            var inner = raw[1..^1];
            var items = inner.Split(", ");
            for (int i = 0; i < items.Length; i++)
            {
                var item = items[i].Trim();
                string resolved = "";
                if (gameData.IsLoaded && long.TryParse(item, out var idx) && idx >= 0)
                    if (gameData.TryResolve((int)idx, out var u, out var d))
                        resolved = $"  → {u} — {d}";
                sb.AppendLine($"  [{i}] {item}{resolved}");
            }
        }
        else if (raw.StartsWith('{') && raw.EndsWith('}'))
        {
            var inner = raw[1..^1];
            foreach (var pair in inner.Split(", "))
                sb.AppendLine($"  {pair}");
        }
        else
        {
            sb.AppendLine($"  {raw}");
        }

        return sb.ToString().TrimEnd();
    }
}
