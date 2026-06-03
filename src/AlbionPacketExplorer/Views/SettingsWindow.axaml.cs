using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using AlbionPacketExplorer.ViewModels;
using AlbionPacketExplorer.Controls;

namespace AlbionPacketExplorer.Views;

public partial class SettingsWindow : ApxWindow
{
    private StackPanel? _sectionDisplay;
    private StackPanel? _sectionCapture;
    private StackPanel? _sectionShortcuts;
    private StackPanel? _sectionPaths;
    private StackPanel? _sectionSchema;
    private StackPanel? _sectionTheme;
    private StackPanel? _sectionGlossary;
    private StackPanel? _sectionAbout;
    private Button? _activeNavButton;

    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        vm.SetClipboard(Clipboard);
        vm.SaveExportRequested += OnSaveExportRequested;
        Loaded += (_, _) =>
        {
            _sectionDisplay   = this.FindControl<StackPanel>("SectionDisplay");
            _sectionCapture   = this.FindControl<StackPanel>("SectionCapture");
            _sectionShortcuts = this.FindControl<StackPanel>("SectionShortcuts");
            _sectionPaths     = this.FindControl<StackPanel>("SectionPaths");
            _sectionSchema  = this.FindControl<StackPanel>("SectionSchema");
            _sectionTheme   = this.FindControl<StackPanel>("SectionTheme");
            _sectionGlossary = this.FindControl<StackPanel>("SectionGlossary");
            _sectionAbout   = this.FindControl<StackPanel>("SectionAbout");

            // Highlight Display as default active
            var defaultBtn = this.FindControl<Button>("NavDisplay");
            if (defaultBtn != null) SetActiveNav(defaultBtn);
        };
    }

    private void SetActiveNav(Button btn)
    {
        if (_activeNavButton != null)
            _activeNavButton.Classes.Remove("navactive");
        _activeNavButton = btn;
        btn.Classes.Add("navactive");
    }

    private void OnNavClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        var tag = btn.Tag as string;
        if (_sectionDisplay   != null) _sectionDisplay.IsVisible   = tag == "Display";
        if (_sectionCapture   != null) _sectionCapture.IsVisible   = tag == "Capture";
        if (_sectionShortcuts != null) _sectionShortcuts.IsVisible = tag == "Shortcuts";
        if (_sectionPaths     != null) _sectionPaths.IsVisible     = tag == "Paths";
        if (_sectionSchema  != null) _sectionSchema.IsVisible  = tag == "Schema";
        if (_sectionTheme   != null) _sectionTheme.IsVisible   = tag == "Theme";
        if (_sectionGlossary != null) _sectionGlossary.IsVisible = tag == "Glossary";
        if (_sectionAbout   != null) _sectionAbout.IsVisible   = tag == "About";
        SetActiveNav(btn);
    }

    private async void OnSaveExportRequested(string suggestedName, string content)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save schema JSON",
            SuggestedFileName = suggestedName,
            DefaultExtension = "json",
            FileTypeChoices = [new FilePickerFileType("JSON files") { Patterns = ["*.json"] }]
        });
        var path = file?.TryGetLocalPath();
        if (path != null)
            await File.WriteAllTextAsync(path, content);
    }
}
