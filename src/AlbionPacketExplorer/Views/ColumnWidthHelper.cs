using Avalonia.Controls;
using Avalonia.Threading;
using AlbionPacketExplorer.Services;

namespace AlbionPacketExplorer.Views;

internal static class ColumnWidthHelper
{
    public static void Restore(DataGrid grid, string key)
    {
        var widths = LayoutStore.Load().GetColumnWidths(key);
        if (widths == null) return;

        Dispatcher.UIThread.Post(() =>
        {
            var cols = grid.Columns;
            for (int i = 0; i < Math.Min(cols.Count, widths.Length); i++)
            {
                if (widths[i] > 0)
                    cols[i].Width = new DataGridLength(widths[i], DataGridLengthUnitType.Pixel);
            }
        }, DispatcherPriority.Loaded);
    }

    public static void Save(DataGrid grid, string key)
    {
        var widths = grid.Columns.Select(c => c.ActualWidth).ToArray();
        if (widths.All(w => w <= 0)) return;
        var state = LayoutStore.Load().WithColumnWidths(key, widths);
        LayoutStore.Save(state);
    }
}
