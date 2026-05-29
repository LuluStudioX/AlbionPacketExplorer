using Avalonia.Controls;

namespace AlbionPacketExplorer.Views;

public partial class CodeAggregatorView : UserControl
{
    public CodeAggregatorView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is ViewModels.CodeAggregatorViewModel vm)
            vm.Clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
    }
}
