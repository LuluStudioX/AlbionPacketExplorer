using Avalonia.Controls;

namespace AlbionPacketExplorer.Views;

public partial class CodeAggregatorView : UserControl
{
    public DataGrid Grid => MainGrid;

    public CodeAggregatorView()
    {
        InitializeComponent();
        Loaded   += (_, _) => ColumnWidthHelper.Restore(MainGrid, "aggregator");
        Unloaded += (_, _) => ColumnWidthHelper.Save(MainGrid, "aggregator");
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is ViewModels.CodeAggregatorViewModel vm)
            vm.Clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
    }
}
