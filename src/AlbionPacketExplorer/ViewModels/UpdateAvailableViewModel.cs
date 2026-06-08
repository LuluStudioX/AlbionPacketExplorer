using AlbionPacketExplorer.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AlbionPacketExplorer.ViewModels;

public enum UpdateChoice { NotNow, UpdateNow, Skip }

/// <summary>
/// Backs the "update available" dialog: shows the new version and its changelog, and lets the user
/// install now, skip this version (never prompt for it again), or dismiss until next time.
/// </summary>
public partial class UpdateAvailableViewModel : ObservableObject
{
    public string Version { get; }
    public string Notes { get; }
    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);

    public string Title => Loc.Format("update.dialog.title", Version);
    public string NoNotesText => Loc.T("update.dialog.noNotes");

    public UpdateChoice Choice { get; private set; } = UpdateChoice.NotNow;

    public event Action? CloseRequested;

    public UpdateAvailableViewModel(string version, string? notes)
    {
        Version = version;
        Notes = notes?.Trim() ?? string.Empty;
    }

    [RelayCommand]
    private void UpdateNow() => Pick(UpdateChoice.UpdateNow);

    [RelayCommand]
    private void Skip() => Pick(UpdateChoice.Skip);

    [RelayCommand]
    private void NotNow() => Pick(UpdateChoice.NotNow);

    private void Pick(UpdateChoice choice)
    {
        Choice = choice;
        CloseRequested?.Invoke();
    }
}
