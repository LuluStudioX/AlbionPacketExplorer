using Avalonia.Controls;
using Avalonia.Threading;
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
        Loaded   += (_, _) => ColumnWidthHelper.Restore(MainGrid, "packetlist");
        Unloaded += (_, _) => ColumnWidthHelper.Save(MainGrid, "packetlist");
    }

    private void OnScrollToRowRequested(PacketRow row)
    {
        Dispatcher.UIThread.Post(() =>
        {
            MainGrid.SelectedItem = row;
            MainGrid.ScrollIntoView(row, null);
        }, DispatcherPriority.Background);
    }
}
