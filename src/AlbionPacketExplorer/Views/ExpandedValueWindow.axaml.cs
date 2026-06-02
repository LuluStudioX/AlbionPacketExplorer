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
        var box = this.FindControl<TextBox>("ValueBox");
        if (box != null)
            box.Text = BuildText(row, gameData);
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
