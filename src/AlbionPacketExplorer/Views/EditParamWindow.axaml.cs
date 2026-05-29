using AlbionPacketExplorer.ViewModels;
using SukiUI.Controls;

namespace AlbionPacketExplorer.Views;

public partial class EditParamWindow : SukiWindow
{
    public EditParamWindow(EditParamViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}
