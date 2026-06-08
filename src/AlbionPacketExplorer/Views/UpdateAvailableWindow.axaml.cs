using AlbionPacketExplorer.Controls;
using AlbionPacketExplorer.ViewModels;

namespace AlbionPacketExplorer.Views;

public partial class UpdateAvailableWindow : ApxWindow
{
    public UpdateAvailableWindow(UpdateAvailableViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        vm.CloseRequested += Close;
    }
}
