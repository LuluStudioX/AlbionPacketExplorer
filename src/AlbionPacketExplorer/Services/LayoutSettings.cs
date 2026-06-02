using System.Text.Json;

namespace AlbionPacketExplorer.Services;

public record LayoutState(
    double TopPanelHeight,
    double LeftPanelWidth,
    double FocusTopHeight = 160,
    double FocusMidHeight = 220,
    Dictionary<string, double[]>? ColumnWidths = null,
    double LeftPanelFraction = 0.66,
    double? WindowX = null,
    double? WindowY = null,
    double? WindowWidth = null,
    double? WindowHeight = null,
    bool WindowMaximized = false)
{
    public static readonly LayoutState Default = new(320, 900);

    public bool HasWindowBounds =>
        WindowX is not null && WindowY is not null &&
        WindowWidth is > 0 && WindowHeight is > 0;

    public double[]? GetColumnWidths(string gridKey) =>
        ColumnWidths?.TryGetValue(gridKey, out var w) == true ? w : null;

    public LayoutState WithColumnWidths(string gridKey, double[] widths)
    {
        var dict = new Dictionary<string, double[]>(ColumnWidths ?? []) { [gridKey] = widths };
        return this with { ColumnWidths = dict };
    }
}

public static class LayoutStore
{
    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AlbionPacketExplorer",
        "layout.json");

    public static LayoutState Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return LayoutState.Default;
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<LayoutState>(json) ?? LayoutState.Default;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"LayoutStore.Load failed: {ex.Message}");
            return LayoutState.Default;
        }
    }

    public static void Save(LayoutState state)
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath)
                ?? throw new InvalidOperationException($"Cannot determine directory for {FilePath}");
            Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(state));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"LayoutStore.Save failed: {ex.Message}");
        }
    }
}
