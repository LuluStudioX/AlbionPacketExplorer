namespace AlbionPacketExplorer.Network.Handlers;

public interface IPacketHandler
{
    Task HandleAsync(object request);
}
