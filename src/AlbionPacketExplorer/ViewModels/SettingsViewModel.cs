using System.Diagnostics;
using System.Reflection;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AlbionPacketExplorer.Services;

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

    public bool MinimizeToTray
    {
        get => _main.MinimizeToTray;
        set => _main.MinimizeToTray = value;
    }

    public bool ForceExpandRows
    {
        get => _main.ForceExpandRows;
        set => _main.ForceExpandRows = value;
    }

    public bool AutoStartCapture
    {
        get => _main.AutoStartCapture;
        set => _main.AutoStartCapture = value;
    }

    public bool AutoSaveLogs
    {
        get => _main.AutoSaveLogs;
        set => _main.AutoSaveLogs = value;
    }

    public IReadOnlyList<DetailDensity> DensityOptions { get; } =
        [DetailDensity.Compact, DetailDensity.Normal, DetailDensity.Comfortable];

    public DetailDensity Density
    {
        get => _main.Density;
        set { _main.Density = value; OnPropertyChanged(); }
    }

    public string DataFolderPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AlbionPacketExplorer");

    public string SatPacketSnifferPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "StatisticsAnalysisTool", "Instances");

    public string ItemCachePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AlbionPacketExplorer", "items.json");

    [RelayCommand]
    private void OpenDataFolder() => OpenInExplorer(DataFolderPath);

    [RelayCommand]
    private void OpenSatFolder() => OpenInExplorer(SatPacketSnifferPath);

    [RelayCommand]
    private void OpenItemCacheFolder() => OpenInExplorer(DataFolderPath);

    private static void OpenInExplorer(string path)
    {
        try
        {
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            if (OperatingSystem.IsWindows())
                Process.Start("explorer.exe", path);
            else if (OperatingSystem.IsMacOS())
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

    // Theme
    private readonly ThemeService _theme = ThemeService.Instance;

    public IReadOnlyList<AccentTheme> AvailableThemes => _theme.Accents;

    public bool IsDarkMode
    {
        get => _theme.IsDark;
        set { _theme.IsDark = value; OnPropertyChanged(); }
    }

    public AccentTheme? SelectedTheme
    {
        get => _theme.ActiveAccent;
        set
        {
            if (value != null) _theme.ActiveAccent = value;
            OnPropertyChanged();
        }
    }

    [RelayCommand]
    private void ToggleDarkMode() => IsDarkMode = !IsDarkMode;

    public SettingsViewModel(MainViewModel main)
    {
        _main = main;
        _theme.Changed += () =>
        {
            OnPropertyChanged(nameof(IsDarkMode));
            OnPropertyChanged(nameof(SelectedTheme));
        };
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
                case nameof(MainViewModel.MinimizeToTray):
                    OnPropertyChanged(nameof(MinimizeToTray));
                    break;
                case nameof(MainViewModel.AutoStartCapture):
                    OnPropertyChanged(nameof(AutoStartCapture));
                    break;
                case nameof(MainViewModel.AutoSaveLogs):
                    OnPropertyChanged(nameof(AutoSaveLogs));
                    break;
                case nameof(MainViewModel.Density):
                    OnPropertyChanged(nameof(Density));
                    break;
            }
        };
    }
}
