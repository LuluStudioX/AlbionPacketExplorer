using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;

namespace AlbionPacketExplorer.Controls;

/// <summary>
/// In-house window base with a custom title bar (logo, title, and
/// minimize/maximize/close caption buttons). Replaces SukiUI's SukiWindow.
///
/// Chrome is drawn by the control template (Themes/ApxWindow.axaml). The client
/// area is extended into the decorations; drag, resize, and the caption-button
/// actions are driven by the platform through the
/// <see cref="Avalonia.Controls.Chrome.WindowDecorationProperties.ElementRoleProperty"/>
/// attached property set on the template elements (TitleBar / Minimize / Maximize /
/// Close / resize grips). Cross-platform via Avalonia only.
/// </summary>
public class ApxWindow : Window
{
    public static readonly StyledProperty<object?> LogoContentProperty =
        AvaloniaProperty.Register<ApxWindow, object?>(nameof(LogoContent));

    public object? LogoContent
    {
        get => GetValue(LogoContentProperty);
        set => SetValue(LogoContentProperty, value);
    }

    public static readonly StyledProperty<bool> IsTitleBarVisibleProperty =
        AvaloniaProperty.Register<ApxWindow, bool>(nameof(IsTitleBarVisible), true);

    public bool IsTitleBarVisible
    {
        get => GetValue(IsTitleBarVisibleProperty);
        set => SetValue(IsTitleBarVisibleProperty, value);
    }

    /// <summary>Optional controls hosted at the right edge of the title bar (before caption buttons).</summary>
    public static readonly StyledProperty<object?> TitleBarContentProperty =
        AvaloniaProperty.Register<ApxWindow, object?>(nameof(TitleBarContent));

    public object? TitleBarContent
    {
        get => GetValue(TitleBarContentProperty);
        set => SetValue(TitleBarContentProperty, value);
    }

    public ApxWindow()
    {
        // Draw our own chrome into the client area; keep the platform border + resize
        // grips so the window stays resizable. Element roles in the template tell the
        // platform which parts are the title bar / caption buttons / resize edges.
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = -1;
        WindowDecorations = WindowDecorations.BorderOnly;

        // Force Dark until ThemeService (commit 4) owns the variant, so the Apx token
        // ThemeDictionaries resolve instead of falling back to the light defaults.
        RequestedThemeVariant = ThemeVariant.Dark;

        // The {x:Type}-keyed chrome ControlTheme is not picked up implicitly while
        // SukiUI's global Window styling is still active (it wins at Style priority).
        // We resolve and assign it directly; it renders correctly once SukiUI is
        // removed (commit 5).
        if (Application.Current?.TryGetResource("ApxWindowTheme", ThemeVariant.Dark, out var theme) == true
            && theme is ControlTheme ct)
        {
            Theme = ct;
        }
    }
}
