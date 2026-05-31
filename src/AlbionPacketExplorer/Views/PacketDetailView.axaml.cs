using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using AlbionPacketExplorer.ViewModels;

namespace AlbionPacketExplorer.Views;

public partial class PacketDetailView : UserControl
{
    public DataGrid Grid => MainGrid;

    private DataGridTemplateColumn? _previewColumn;

    public PacketDetailView()
    {
        InitializeComponent();
        Loaded   += (_, _) => ColumnWidthHelper.Restore(MainGrid, "packetdetail");
        Unloaded += (_, _) => ColumnWidthHelper.Save(MainGrid, "packetdetail");
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is PacketDetailViewModel vm)
        {
            vm.PreviewResolveToggled += OnPreviewResolveToggled;
        }
    }

    private void OnPreviewResolveToggled()
    {
        if (DataContext is not PacketDetailViewModel vm) return;

        if (vm.IsPreviewActive)
        {
            _previewColumn = new DataGridTemplateColumn
            {
                Header = "Resolved Preview",
                Width = new DataGridLength(2, DataGridLengthUnitType.Star),
                MinWidth = 160,
                CellTemplate = new FuncDataTemplate<ParamRow>((row, _) =>
                {
                    var panel = new StackPanel { Margin = new Avalonia.Thickness(4, 2) };

                    // Single item row
                    var single = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6 };
                    single[!Avalonia.Controls.Control.IsVisibleProperty] = new Binding(nameof(ParamRow.HasSingleResolved));
                    var singleIcon = new Avalonia.Controls.Image { Width = 28, Height = 28 };
                    singleIcon[!Avalonia.Controls.Image.SourceProperty] = new Binding(nameof(ParamRow.Icon));
                    singleIcon[!Avalonia.Controls.Control.IsVisibleProperty] = new Binding(nameof(ParamRow.Icon)) { Converter = Avalonia.Data.Converters.ObjectConverters.IsNotNull };
                    var singleText = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
                    singleText[!TextBlock.TextProperty] = new Binding(nameof(ParamRow.ResolvedName));
                    single.Children.Add(singleIcon);
                    single.Children.Add(singleText);

                    // Multi item rows
                    var multi = new Avalonia.Controls.ItemsControl();
                    multi[!Avalonia.Controls.Control.IsVisibleProperty] = new Binding(nameof(ParamRow.HasResolvedItems));
                    multi[!Avalonia.Controls.ItemsControl.ItemsSourceProperty] = new Binding(nameof(ParamRow.ResolvedItems));
                    multi.ItemTemplate = new FuncDataTemplate<ResolvedItem>((_, _2) =>
                    {
                        var row2 = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 4, Margin = new Avalonia.Thickness(0, 1, 0, 1) };
                        var icon2 = new Avalonia.Controls.Image { Width = 22, Height = 22 };
                        icon2[!Avalonia.Controls.Image.SourceProperty] = new Binding(nameof(ResolvedItem.Icon));
                        icon2[!Avalonia.Controls.Control.IsVisibleProperty] = new Binding(nameof(ResolvedItem.Icon)) { Converter = Avalonia.Data.Converters.ObjectConverters.IsNotNull };
                        var name2 = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
                        name2[!TextBlock.TextProperty] = new Binding(nameof(ResolvedItem.DisplayName));
                        row2.Children.Add(icon2);
                        row2.Children.Add(name2);
                        return row2;
                    }, true);

                    // Fallback text for non-indexed values
                    var fallback = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
                    fallback[!TextBlock.TextProperty] = new Binding(nameof(ParamRow.PreviewText));
                    fallback[!Avalonia.Controls.Control.IsVisibleProperty] = new Binding(nameof(ParamRow.HasResolved)) { Converter = Avalonia.Data.Converters.BoolConverters.Not };

                    panel.Children.Add(single);
                    panel.Children.Add(multi);
                    panel.Children.Add(fallback);
                    return panel;
                }, true)
            };
            // Insert between Value (2) and Resolved (3)
            MainGrid.Columns.Insert(3, _previewColumn);
        }
        else
        {
            if (_previewColumn != null)
            {
                MainGrid.Columns.Remove(_previewColumn);
                _previewColumn = null;
            }
        }
    }
}
