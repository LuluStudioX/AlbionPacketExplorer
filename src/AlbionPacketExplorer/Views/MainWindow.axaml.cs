using Avalonia.Controls;
using Avalonia.Platform.Storage;
using AlbionPacketExplorer.Services;
using AlbionPacketExplorer.ViewModels;

namespace AlbionPacketExplorer.Views;

public partial class MainWindow : Window, IFilePicker
{
    private Grid? _mainGrid;
    private Grid? _bottomGrid;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel(this);
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    public async Task<string?> PickJsonFileAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open packet_sniffer.json",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("JSON files") { Patterns = ["*.json"] },
                new FilePickerFileType("All files") { Patterns = ["*"] }
            ]
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        _mainGrid = this.FindControl<Grid>("MainGrid");
        _bottomGrid = this.FindControl<Grid>("BottomGrid");

        var layout = LayoutStore.Load();

        if (_mainGrid != null)
            _mainGrid.RowDefinitions[0] = new RowDefinition(layout.TopPanelHeight, GridUnitType.Pixel);

        if (_bottomGrid != null)
            _bottomGrid.ColumnDefinitions[0] = new ColumnDefinition(layout.LeftPanelWidth, GridUnitType.Pixel);
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_mainGrid == null || _bottomGrid == null) return;

        LayoutStore.Save(new LayoutState(
            _mainGrid.RowDefinitions[0].ActualHeight,
            _bottomGrid.ColumnDefinitions[0].ActualWidth));
    }
}
