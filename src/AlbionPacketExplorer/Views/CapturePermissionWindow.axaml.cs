using AlbionPacketExplorer.Controls;
using AlbionPacketExplorer.ViewModels;

namespace AlbionPacketExplorer.Views;

public partial class CapturePermissionWindow : ApxWindow
{
    public CapturePermissionWindow(CapturePermissionViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        vm.CloseRequested += Close;
    }
}
