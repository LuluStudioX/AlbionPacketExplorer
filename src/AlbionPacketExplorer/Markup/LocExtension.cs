using System.ComponentModel;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using AlbionPacketExplorer.Services;

namespace AlbionPacketExplorer.Markup;

/// <summary>
/// Shared binding source that exposes the active string table through an indexer and re-raises a
/// change notification whenever the culture changes, so every <c>{loc:T key}</c> binding refreshes
/// live without rebuilding the view.
/// </summary>
public sealed class LocSource : INotifyPropertyChanged
{
    public static LocSource Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    private LocSource()
    {
        LocalizationService.Instance.CultureChanged += () =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    public string this[string key] => LocalizationService.Instance[key];
}

/// <summary>
/// XAML markup extension: <c>Text="{loc:T nav.capture}"</c>. Produces a one-way binding to the
/// shared <see cref="LocSource"/> indexer so the resolved string updates when the culture changes.
/// </summary>
public sealed class TExtension : MarkupExtension
{
    public TExtension() { }

    public TExtension(string key) => Key = key;

    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]")
        {
            Source = LocSource.Instance,
            Mode = BindingMode.OneWay
        };
}
