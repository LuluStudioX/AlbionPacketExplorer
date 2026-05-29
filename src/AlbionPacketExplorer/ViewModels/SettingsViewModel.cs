using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
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
