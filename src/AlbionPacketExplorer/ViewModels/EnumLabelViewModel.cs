using AlbionPacketExplorer.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AlbionPacketExplorer.ViewModels;

/// <summary>Edits the user label for one specific field value (enum-value catalog).</summary>
public partial class EnumLabelViewModel : ObservableObject
{
    private readonly EnumLabelStore _store;
    private readonly string _kind;
    private readonly int _code;
    private readonly string _key;
    private readonly string _value;
    private readonly Action _onSaved;

    [ObservableProperty] private string _label = string.Empty;

    public string Title { get; }
    public string ValueText { get; }

    public EnumLabelViewModel(EnumLabelStore store, string kind, int code, string key,
                              string value, string currentLabel, Action onSaved)
    {
        _store = store;
        _kind = kind;
        _code = code;
        _key = key;
        _value = value;
        _onSaved = onSaved;
        _label = currentLabel;
        Title = Loc.Format("enumLabel.title", kind, code.ToString(), key);
        ValueText = value;
    }

    public event Action? CloseRequested;

    [RelayCommand]
    private void Save()
    {
        _store.Set(_kind, _code, _key, _value, Label);
        _onSaved();
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void Clear()
    {
        _store.Set(_kind, _code, _key, _value, null);
        _onSaved();
        CloseRequested?.Invoke();
    }
}
