using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AlbionPacketExplorer.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public string Version { get; } =
        "v" + (Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "unknown");

    public bool GameDataLoaded => _main.GameDataLoaded;

    public bool ResolveItemNames
    {
        get => _main.ResolveItemNames;
        set => _main.ResolveItemNames = value;
    }

    public bool ResolveIcons
    {
        get => _main.ResolveIcons;
        set => _main.ResolveIcons = value;
    }

    public string DataFolderPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AlbionPacketExplorer");

    public string SatPacketSnifferPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        @"StatisticsAnalysisTool\Instances");

    public string ItemCachePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AlbionPacketExplorer", "items.json");

    [RelayCommand]
    private void OpenDataFolder() => OpenInExplorer(DataFolderPath);

    [RelayCommand]
    private void OpenSatFolder() => OpenInExplorer(SatPacketSnifferPath);

    private static void OpenInExplorer(string path)
    {
        try
        {
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Process.Start("explorer.exe", path);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                Process.Start("open", path);
            else
                Process.Start("xdg-open", path);
        }
        catch { }
    }

    // Schema export
    [ObservableProperty] private string _exportKind = "EVENT";
    [ObservableProperty] private string _exportCode = string.Empty;
    [ObservableProperty] private string _exportPreview = string.Empty;

    partial void OnExportKindChanged(string value) => RefreshPreview();
    partial void OnExportCodeChanged(string value) => RefreshPreview();

    private void RefreshPreview()
    {
        if (!int.TryParse(ExportCode, out var code)) { ExportPreview = string.Empty; return; }
        ExportPreview = _main.Schema.ExportEventSchema(ExportKind, code);
    }

    [RelayCommand]
    private async Task CopyExportAsync()
    {
        if (string.IsNullOrEmpty(ExportPreview)) return;
        if (_clipboard != null)
            await _clipboard.SetTextAsync(ExportPreview);
    }

    public void SetClipboard(IClipboard? clipboard) => _clipboard = clipboard;
    private IClipboard? _clipboard;

    public event Action<string, string>? SaveExportRequested;

    [RelayCommand]
    private void SaveExport()
    {
        if (string.IsNullOrEmpty(ExportPreview)) return;
        var kind = ExportKind.ToUpperInvariant();
        var code = ExportCode;
        SaveExportRequested?.Invoke($"{kind}_{code}_schema.json", ExportPreview);
    }

    public SettingsViewModel(MainViewModel main)
    {
        _main = main;
        _main.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(MainViewModel.GameDataLoaded):
                    OnPropertyChanged(nameof(GameDataLoaded));
                    break;
                case nameof(MainViewModel.ResolveItemNames):
                    OnPropertyChanged(nameof(ResolveItemNames));
                    break;
                case nameof(MainViewModel.ResolveIcons):
                    OnPropertyChanged(nameof(ResolveIcons));
                    break;
            }
        };
    }
}
