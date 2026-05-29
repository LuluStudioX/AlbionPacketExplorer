namespace AlbionPacketExplorer.Services;

public interface IFilePicker
{
    Task<string?> PickJsonFileAsync();
    Task<string?> PickSaveJsonFileAsync(string suggestedName);
}
