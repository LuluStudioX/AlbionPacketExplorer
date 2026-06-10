using System.Text;
using AlbionPacketExplorer.Models;
using AlbionPacketExplorer.Services;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AlbionPacketExplorer.ViewModels;

/// <summary>
/// Drives the Share Field Data window: builds a privacy-filtered schema digest from the
/// aggregated session stats, previews exactly what would leave the machine, and offers
/// save-to-file, copy, or direct upload. Nothing is sent without one of those explicit clicks.
/// </summary>
public partial class ShareDigestViewModel : ObservableObject
{
    private readonly IReadOnlyCollection<CodeStats> _stats;
    private readonly PacketSchemaService? _schema;
    private readonly string _appVersion;

    private SchemaDigest _digest = new();
    private string _json = "";

    /// <summary>Supplied by the view: save picker seeded with a suggested file name.</summary>
    public Func<string, Task<string?>>? RequestSavePath;

    /// <summary>Raised after a successful upload so the window closes itself.</summary>
    public event Action? CloseRequested;

    public IClipboard? Clipboard { get; set; }

    [ObservableProperty] private bool _unknownOnly = true;
    [ObservableProperty] private string _summaryText = "";
    [ObservableProperty] private string _previewText = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isUploading;

    public ShareDigestViewModel(
        IReadOnlyCollection<CodeStats> stats,
        PacketSchemaService? schema,
        string appVersion)
    {
        _stats = stats;
        _schema = schema;
        _appVersion = appVersion;
        Rebuild();
    }

    partial void OnUnknownOnlyChanged(bool value) => Rebuild();

    private void Rebuild()
    {
        _digest = SchemaDigestBuilder.Build(_stats, _schema, UnknownOnly, _appVersion);
        _json = SchemaDigestBuilder.ToJson(_digest);

        var keyCount = _digest.Codes.Sum(c => c.Keys.Count);
        SummaryText = Loc.Format("digest.summary",
            _digest.Codes.Count, keyCount, ToolsInputItem.FormatBytes(Encoding.UTF8.GetByteCount(_json)));

        // The preview shows the indented form so each field is inspectable; capped so a
        // full-session "all codes" digest cannot stall the TextBox.
        var pretty = SchemaDigestBuilder.ToJson(_digest, indented: true);
        PreviewText = pretty.Length <= 60_000 ? pretty : pretty[..60_000] + "\n…";

        StatusText = "";
        UploadCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        CopyCommand.NotifyCanExecuteChanged();
    }

    private bool HasCodes() => _digest.Codes.Count > 0;

    [RelayCommand(CanExecute = nameof(HasCodes))]
    private async Task SaveAsync()
    {
        if (RequestSavePath is null) return;
        var suggested = $"apx-digest-{_digest.CreatedAt}.json";
        var path = await RequestSavePath(suggested);
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            await File.WriteAllTextAsync(path, _json, Encoding.UTF8);
            StatusText = Loc.Format("digest.status.saved", Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            StatusText = Loc.Format("digest.status.error", ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(HasCodes))]
    private async Task CopyAsync()
    {
        if (Clipboard == null) return;
        await Clipboard.SetTextAsync(_json);
        StatusText = Loc.T("digest.status.copied");
    }

    private bool CanUpload() => HasCodes() && !IsUploading;

    [RelayCommand(CanExecute = nameof(CanUpload))]
    private async Task UploadAsync()
    {
        IsUploading = true;
        StatusText = Loc.T("digest.status.uploading");
        try
        {
            var result = await DigestUploadService.UploadAsync(_json);
            StatusText = result.Ok
                ? Loc.T("digest.status.uploaded")
                : Loc.Format("digest.status.error", result.Message);

            if (result.Ok)
            {
                // Leave the thank-you visible for a beat, then close.
                await Task.Delay(1200);
                CloseRequested?.Invoke();
            }
        }
        finally
        {
            IsUploading = false;
            UploadCommand.NotifyCanExecuteChanged();
        }
    }
}
