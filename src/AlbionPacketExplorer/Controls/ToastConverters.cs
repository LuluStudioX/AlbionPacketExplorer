using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;
using AlbionPacketExplorer.Services;

namespace AlbionPacketExplorer.Controls;

public static class ToastConverters
{
    /// <summary>Maps a toast severity to its accent stripe brush, resolved from the active theme.</summary>
    public static readonly IValueConverter SeverityToBrush =
        new FuncValueConverter<ToastSeverity, IBrush?>(s => Brush(s switch
        {
            ToastSeverity.Success => "Apx.Success",
            ToastSeverity.Warning => "Apx.Warning",
            ToastSeverity.Error   => "Apx.Danger",
            _                     => "Apx.Accent",
        }));

    private static IBrush? Brush(string key) =>
        Avalonia.Application.Current?.TryGetResource(key, ThemeVariant.Dark, out var v) == true
            ? v as IBrush
            : null;
}
