using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AlbionPacketExplorer.Services;
using AlbionPacketExplorer.ViewModels;

namespace AlbionPacketExplorer.Views;

public partial class PacketListView : UserControl
{
    public DataGrid Grid => MainGrid;

    private PacketListViewModel? _vm;

    public PacketListView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm != null) _vm.ScrollToRowRequested -= OnScrollToRowRequested;
            _vm = DataContext as PacketListViewModel;
            if (_vm != null) _vm.ScrollToRowRequested += OnScrollToRowRequested;
        };
        Loaded   += OnLoaded;
        Unloaded += (_, _) => ColumnWidthHelper.Save(MainGrid, "packetlist");
    }

    private bool _columnsMenuAdded;

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        ColumnWidthHelper.Restore(MainGrid, "packetlist");
        ColumnVisibilityHelper.Restore(MainGrid, "packetlist");

        // Append a "Columns" toggle submenu to the grid's context menu, once.
        if (!_columnsMenuAdded && MainGrid.ContextMenu is { } menu)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(ColumnVisibilityHelper.BuildMenu(MainGrid, "packetlist", Loc.T("list.menu.columns")));
            _columnsMenuAdded = true;
        }
    }

    private void OnScrollToRowRequested(PacketRow row)
    {
        Dispatcher.UIThread.Post(() =>
        {
            MainGrid.SelectedItem = row;
            MainGrid.ScrollIntoView(row, null);
        }, DispatcherPriority.Background);
    }

    // Diff the two selected packets. The DataGrid's SelectedItems is not bindable, so the
    // selection is read here and handed to the view model in pick order.
    private void OnDiffSelectedClick(object? sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        var picked = MainGrid.SelectedItems.OfType<PacketRow>().ToList();
        if (picked.Count != 2)
        {
            _vm.Toasts?.Show(Loc.T("toast.diff.title"), Loc.T("toast.diff.needTwo"), ToastSeverity.Info);
            return;
        }
        _vm.RequestDiff(picked[0].Packet, picked[1].Packet);
    }
}
