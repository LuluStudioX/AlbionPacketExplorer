using System.IO;
using System.Threading.Tasks;
using AlbionPacketExplorer.Services;
using Xunit;

namespace AlbionPacketExplorer.Tests;

public class PacketFileReaderTests
{
    private const string Packet =
        "{\"ts\":\"2026-07-03T00:00:00Z\",\"kind\":\"EVENT\",\"code\":3,\"params\":{}}";

    private static async Task<int> CountAsync(string path)
    {
        var reader = new PacketFileReader();
        int n = 0;
        await foreach (var _ in reader.ReadAsync(path)) n++;
        return n;
    }

    [Fact]
    public async Task Truncated_array_file_salvages_the_valid_prefix()
    {
        var path = Path.GetTempFileName();
        try
        {
            // Two complete packets, then a third torn mid-Byte[] with the array left unclosed -
            // exactly the shape of a capture killed mid-write. The reader must yield the two good
            // ones instead of throwing the whole file away.
            var torn = "{\"ts\":\"2026-07-03T00:00:01Z\",\"kind\":\"EVENT\",\"code\":3,"
                     + "\"params\":{\"1\":{\"type\":\"Byte[]\",\"value\":[3";
            await File.WriteAllTextAsync(path, "[\n" + Packet + ",\n" + Packet + ",\n" + torn);

            Assert.Equal(2, await CountAsync(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Well_formed_array_reads_every_element()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "[\n" + Packet + ",\n" + Packet + ",\n" + Packet + "\n]");
            Assert.Equal(3, await CountAsync(path));
        }
        finally { File.Delete(path); }
    }
}
