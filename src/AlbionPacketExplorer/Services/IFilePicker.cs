namespace AlbionPacketExplorer.Services;

public interface IFilePicker
{
    Task<string?> PickJsonFileAsync();
    Task<string?> PickSaveJsonFileAsync(string suggestedName);
    Task<string?> PickFolderAsync(string title);

    /// <summary>Open a capture in any supported format (JSON or raw .b64); null if cancelled.</summary>
    Task<string?> PickOpenFileAsync();

    /// <summary>Save dialog for an arbitrary format. <paramref name="extension"/> e.g. "csv".</summary>
    Task<string?> PickSaveFileAsync(string suggestedName, string extension, string typeName);
}
