namespace AlbionPacketExplorer.Services;

public interface IFilePicker
{
    Task<string?> PickJsonFileAsync();
}
