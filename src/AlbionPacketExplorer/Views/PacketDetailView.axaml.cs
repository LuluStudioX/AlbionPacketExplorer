using Avalonia.Controls;
using AlbionPacketExplorer.ViewModels;

namespace AlbionPacketExplorer.Views;

public partial class PacketDetailView : UserControl
{
    public PacketDetailView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is PacketDetailViewModel vm)
            vm.EditParamRequested += OnEditParamRequested;
    }

    private void OnEditParamRequested(EditParamViewModel vm)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        var win = new EditParamWindow(vm);
        win.Show(owner);
    }
}
