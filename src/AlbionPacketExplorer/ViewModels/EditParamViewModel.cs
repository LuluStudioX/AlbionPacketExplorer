using AlbionPacketExplorer.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using static AlbionPacketExplorer.Services.PacketSchemaService;

namespace AlbionPacketExplorer.ViewModels;

public partial class EditParamViewModel : ObservableObject
{
    private readonly PacketSchemaService _schema;
    private readonly string _kind;
    private readonly int _code;
    private readonly string _key;
    private readonly Action _onSaved;
    private readonly IReadOnlyList<string> _allKnownNames;

    [ObservableProperty] private string _paramName = string.Empty;
    [ObservableProperty] private string _note = string.Empty;
    [ObservableProperty] private string _resolveAs = string.Empty;
    [ObservableProperty] private ObservableCollection<string> _suggestions = [];
    [ObservableProperty] private bool _showSuggestions;

    public static readonly string[] ResolveAsOptions = ["", "itemIndex"];

    public string Title { get; }
    public bool SourceIsBase { get; }
    public bool SourceIsUser { get; }

    public EditParamViewModel(PacketSchemaService schema, string kind, int code,
                               string key, string currentName, string currentNote,
                               string currentResolveAs, Action onSaved,
                               ParamSource source = ParamSource.None)
    {
        _schema = schema;
        _kind = kind;
        _code = code;
        _key = key;
        _onSaved = onSaved;
        _allKnownNames = schema.GetAllKnownParamNames();

        _paramName = currentName;
        _note = currentNote;
        _resolveAs = currentResolveAs;
        Title = $"Edit param {key} — {kind} {code}";
        SourceIsBase = source == ParamSource.Base;
        SourceIsUser = source == ParamSource.User;
    }

    partial void OnParamNameChanged(string value)
    {
        var q = value.Trim();
        if (string.IsNullOrEmpty(q))
        {
            Suggestions = [];
            ShowSuggestions = false;
            return;
        }
        var matches = _allKnownNames
            .Where(n => n.Contains(q, StringComparison.OrdinalIgnoreCase))
            .Take(10)
            .ToList();
        Suggestions = new ObservableCollection<string>(matches);
        ShowSuggestions = matches.Count > 0;
    }

    [RelayCommand]
    private void AcceptSuggestion(string name)
    {
        ParamName = name;
        Suggestions = [];
        ShowSuggestions = false;
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
