using System.Threading.Channels;
using AlbionPacketExplorer.Models;

namespace AlbionPacketExplorer.Services;

/// <summary>
/// Parallel packet-statistics aggregation for the file-load path, exact by construction.
///
/// <para>Stats for different (kind, code) buckets are fully independent, so packets are routed by a
/// hash of (kind, code) to one of N shard workers; each shard runs the SAME sequential
/// <see cref="CodeStatsMap"/> ingest over its disjoint subset of codes, in file order (batches are
/// fed in file order and each shard's channel is FIFO). The shard results are a disjoint union -
/// no merging of counts ever happens, so the final state is bit-identical to single-threaded
/// ingest of the whole file.</para>
/// </summary>
public sealed class ShardedPacketStats
{
    private readonly int _shardCount;
    private readonly Channel<List<(string Kind, int Code, ParamSet Params)>>[] _channels;
    private readonly Task<CodeStatsMap>[] _workers;

    public ShardedPacketStats(int? shardCount = null)
    {
        // Stats ingest is lighter than parse; a handful of shards is enough to pull it off the
        // critical path. More shards = more routing lists per batch for little gain.
        _shardCount = shardCount ?? Math.Clamp(Environment.ProcessorCount / 2, 2, 6);
        _channels = new Channel<List<(string, int, ParamSet)>>[_shardCount];
        _workers = new Task<CodeStatsMap>[_shardCount];

        for (int i = 0; i < _shardCount; i++)
        {
            var channel = Channel.CreateBounded<List<(string, int, ParamSet)>>(new BoundedChannelOptions(8)
            {
                SingleReader = true,
                SingleWriter = true,
            });
            _channels[i] = channel;
            _workers[i] = Task.Run(async () =>
            {
                var map = new CodeStatsMap();
                await foreach (var sublist in channel.Reader.ReadAllAsync())
                    foreach (var (kind, code, ps) in sublist)
                        map.Ingest(kind, code, ps);
                return map;
            });
        }
    }

    private int ShardOf(string kind, int code) =>
        (int)((uint)HashCode.Combine(kind, code) % (uint)_shardCount);

    /// <summary>
    /// Routes one load batch to the shards. Awaited per batch (bounded channels give backpressure),
    /// and batches must be fed in file order - that is what keeps each shard's per-code stream in
    /// file order and the result exact.
    /// </summary>
    public async ValueTask FeedAsync(IReadOnlyList<(PacketEntry Entry, ParamSet Params)> items)
    {
        var buckets = new List<(string, int, ParamSet)>?[_shardCount];
        foreach (var (entry, ps) in items)
        {
            int s = ShardOf(entry.Kind, entry.Code);
            (buckets[s] ??= new List<(string, int, ParamSet)>(items.Count)).Add((entry.Kind, entry.Code, ps));
        }

        for (int i = 0; i < _shardCount; i++)
            if (buckets[i] is { } b)
                await _channels[i].Writer.WriteAsync(b);
    }

    /// <summary>Completes all shards and returns their disjoint maps for adoption.</summary>
    public async Task<IReadOnlyList<CodeStatsMap>> CompleteAsync()
    {
        foreach (var c in _channels)
            c.Writer.Complete();
        return await Task.WhenAll(_workers);
    }

    /// <summary>
    /// Abandons the shards after a failed load: completes the channels and observes the worker
    /// tasks so nothing is left running or unobserved. Safe to call after CompleteAsync too.
    /// </summary>
    public async Task AbortAsync()
    {
        foreach (var c in _channels)
            c.Writer.TryComplete();
        try { await Task.WhenAll(_workers); } catch { /* abandoned results */ }
    }
}
