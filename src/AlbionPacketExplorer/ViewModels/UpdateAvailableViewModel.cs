using AlbionPacketExplorer.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AlbionPacketExplorer.ViewModels;

// Closed = dialog dismissed via the window's X (keep the toolbar badge); NotNow = the explicit
// "Not now" button (hide the badge for this session). Closed must stay the default (value 0) so a
// window close that runs no command lands here.
public enum UpdateChoice { Closed, NotNow, UpdateNow, Skip }

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

    public UpdateChoice Choice { get; private set; } = UpdateChoice.Closed;

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
