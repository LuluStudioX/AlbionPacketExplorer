using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AlbionPacketExplorer.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public string Version { get; } =
        "v" + (Assembly.GetExecutingAssembly()
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()
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

    public SettingsViewModel(MainViewModel main)
    {
        _main = main;
        _main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.GameDataLoaded))
                OnPropertyChanged(nameof(GameDataLoaded));
        };
    }
}
