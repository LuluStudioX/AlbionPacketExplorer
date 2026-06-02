using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace AlbionPacketExplorer.Services;

/// <summary>A selectable accent palette. Overrides the Apx.Accent* token brushes.</summary>
public sealed record AccentTheme(string DisplayName, Color Accent, Color AccentHover, Color AccentPressed)
{
    public IBrush AccentBrush => new SolidColorBrush(Accent);
}

/// <summary>
/// Owns the application theme: light/dark variant plus the accent palette. Replaces
/// SukiUI's SukiTheme. Variant switching is driven by Avalonia's
/// <see cref="Application.RequestedThemeVariant"/> (the Apx token ThemeDictionaries
/// resolve against it); accent switching rewrites the Apx.Accent* brush resources.
/// </summary>
public sealed class ThemeService
{
    public static ThemeService Instance { get; } = new();

    public IReadOnlyList<AccentTheme> Accents { get; } =
    [
        new("Indigo",  Color.Parse("#6D7BF5"), Color.Parse("#828EF7"), Color.Parse("#5867E0")),
        new("Emerald", Color.Parse("#3FB67A"), Color.Parse("#55C78D"), Color.Parse("#2E9E63")),
        new("Amber",   Color.Parse("#E0A23F"), Color.Parse("#EBB155"), Color.Parse("#C2871F")),
        new("Rose",    Color.Parse("#E05677"), Color.Parse("#EB6E8B"), Color.Parse("#C23E5E")),
        new("Cyan",    Color.Parse("#3FB6C9"), Color.Parse("#55C7D9"), Color.Parse("#2E9EB0")),
    ];

    public event Action? Changed;

    private AccentTheme _activeAccent;
    public AccentTheme ActiveAccent
    {
        get => _activeAccent;
        set
        {
            if (_activeAccent == value) return;
            _activeAccent = value;
            ApplyAccent(value);
            Changed?.Invoke();
        }
    }

    public bool IsDark
    {
        get => Application.Current?.RequestedThemeVariant == ThemeVariant.Dark;
        set
        {
            if (Application.Current is { } app)
                app.RequestedThemeVariant = value ? ThemeVariant.Dark : ThemeVariant.Light;
            Changed?.Invoke();
        }
    }

    private ThemeService() => _activeAccent = Accents[0];

    public void Initialize(bool isDark, string? accentName)
    {
        if (Application.Current is { } app)
            app.RequestedThemeVariant = isDark ? ThemeVariant.Dark : ThemeVariant.Light;

        var accent = Accents.FirstOrDefault(a => a.DisplayName == accentName) ?? Accents[0];
        _activeAccent = accent;
        ApplyAccent(accent);
    }

    private static void ApplyAccent(AccentTheme accent)
    {
        if (Application.Current is not { } app) return;
        app.Resources["Apx.Accent"] = new SolidColorBrush(accent.Accent);
        app.Resources["Apx.AccentHover"] = new SolidColorBrush(accent.AccentHover);
        app.Resources["Apx.AccentPressed"] = new SolidColorBrush(accent.AccentPressed);
    }
}
