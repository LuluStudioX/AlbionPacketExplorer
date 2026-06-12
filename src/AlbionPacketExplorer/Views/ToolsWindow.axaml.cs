using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using AlbionPacketExplorer.Controls;
using AlbionPacketExplorer.ViewModels;

namespace AlbionPacketExplorer.Views;

public partial class ToolsWindow : ApxWindow
{
    public ToolsWindow(ToolsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        vm.RequestPickFiles = PickFilesAsync;
        vm.RequestPickFolder = PickFolderAsync;
        vm.RequestPickOutput = PickOutputAsync;
        vm.RequestPickExisting = PickExistingAsync;
    }

    private async Task<string?> PickExistingAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Verify or load an existing merged file",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Captures (JSON)") { Patterns = ["*.json"] },
                new FilePickerFileType("All files") { Patterns = ["*"] }
            ]
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    private async Task<IReadOnlyList<string>> PickFilesAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add packet capture files",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Captures (JSON)") { Patterns = ["*.json"] },
                new FilePickerFileType("All files") { Patterns = ["*"] }
            ]
        });
        return files.Select(f => f.TryGetLocalPath())
                    .Where(p => !string.IsNullOrEmpty(p))
                    .Select(p => p!)
                    .ToList();
    }

    private async Task<string?> PickFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Add every *.json in folder",
            AllowMultiple = false
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    private async Task<string?> PickOutputAsync(string suggestedName)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Merged output file",
            SuggestedFileName = suggestedName,
            DefaultExtension = "json",
            FileTypeChoices = [new FilePickerFileType("JSON files") { Patterns = ["*.json"] }]
        });
        return file?.TryGetLocalPath();
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();

    private async void OnCopyLogClicked(object? sender, RoutedEventArgs e)
    {
        var log = (DataContext as ToolsViewModel)?.Log;
        if (!string.IsNullOrEmpty(log))
            await Clipboard!.SetTextAsync(log);
    }
}
