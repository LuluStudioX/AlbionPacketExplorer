using AlbionPacketExplorer.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AlbionPacketExplorer.ViewModels;

public partial class EditParamViewModel : ObservableObject
{
    private readonly PacketSchemaService _schema;
    private readonly string _kind;
    private readonly int _code;
    private readonly string _key;
    private readonly Action _onSaved;

    [ObservableProperty] private string _paramName = string.Empty;
    [ObservableProperty] private string _note = string.Empty;
    [ObservableProperty] private string _resolveAs = string.Empty;

    public static readonly string[] ResolveAsOptions = ["", "itemIndex"];

    public string Title { get; }

    public EditParamViewModel(PacketSchemaService schema, string kind, int code,
                               string key, string currentName, string currentNote,
                               string currentResolveAs, Action onSaved)
    {
        _schema = schema;
        _kind = kind;
        _code = code;
        _key = key;
        _onSaved = onSaved;

        _paramName = currentName;
        _note = currentNote;
        _resolveAs = currentResolveAs;
        Title = $"Edit param {key} — {kind} {code}";
    }

    public event Action? CloseRequested;

    [RelayCommand]
    private async Task SaveAsync()
    {
        await _schema.SaveUserParamAsync(_kind, _code, _key, ParamName.Trim(), Note.Trim(), ResolveAs.Trim());
        _onSaved();
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private async Task ClearOverrideAsync()
    {
        await _schema.ClearUserParamAsync(_kind, _code, _key);
        _onSaved();
        CloseRequested?.Invoke();
    }
}
