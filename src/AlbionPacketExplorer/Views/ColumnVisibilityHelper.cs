using Avalonia.Controls;
using Avalonia.Threading;
using AlbionPacketExplorer.Services;

namespace AlbionPacketExplorer.Views;

/// <summary>
/// Persists which DataGrid columns are hidden (per grid key) and builds a checkable "Columns"
/// submenu so the user can toggle them. Mirrors <see cref="ColumnWidthHelper"/>.
/// </summary>
internal static class ColumnVisibilityHelper
{
    public static void Restore(DataGrid grid, string key)
    {
        var hidden = LayoutStore.Load().GetHiddenColumns(key);
        if (hidden is null || hidden.Length == 0) return;

        Dispatcher.UIThread.Post(() =>
        {
            foreach (var i in hidden)
                if (i >= 0 && i < grid.Columns.Count)
                    grid.Columns[i].IsVisible = false;
        }, DispatcherPriority.Loaded);
    }

    /// <summary>A "Columns" menu item whose checkable children toggle and persist visibility.</summary>
    public static MenuItem BuildMenu(DataGrid grid, string key, string header)
    {
        var root = new MenuItem { Header = header };
        for (int i = 0; i < grid.Columns.Count; i++)
        {
            var col = grid.Columns[i];
            var item = new MenuItem
            {
                Header = ColumnLabel(col, i),
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = col.IsVisible,
            };
            item.Click += (_, _) =>
            {
                // Keep at least one column visible so the grid never becomes unusable.
                if (col.IsVisible && grid.Columns.Count(c => c.IsVisible) <= 1)
                {
                    item.IsChecked = true;
                    return;
                }
                col.IsVisible = !col.IsVisible;
                item.IsChecked = col.IsVisible;
                Save(grid, key);
            };
            root.Items.Add(item);
        }
        return root;
    }

    private static string ColumnLabel(DataGridColumn col, int i) =>
        !string.IsNullOrEmpty(col.SortMemberPath) ? col.SortMemberPath
        : col.Header?.ToString() is { Length: > 0 } h ? h
        : $"Column {i + 1}";

    private static void Save(DataGrid grid, string key)
    {
        var hidden = grid.Columns
            .Select((c, i) => (c, i))
            .Where(t => !t.c.IsVisible)
            .Select(t => t.i)
            .ToArray();
        LayoutStore.Save(LayoutStore.Load().WithHiddenColumns(key, hidden));
    }
}
