using System.Threading.Channels;
using AlbionPacketExplorer.Models;

namespace AlbionPacketExplorer.Services;

/// <summary>
/// Parallel packet-statistics aggregation for the file-load path.
///
/// <para>The earlier design sharded by a hash of (kind, code) so shards stayed disjoint and could be
/// unioned without merging. But real captures are heavily skewed - a handful of codes (movement,
/// etc.) hold most packets - so those codes pinned one or two shards while the rest sat idle (~2 of
/// 16 cores busy). This version partitions by ARRIVAL instead: each parse batch is handed round-robin
/// to one of N worker maps, so load spreads evenly across all cores regardless of code skew. Because
/// a single (kind, code) is now split across workers, the workers' maps overlap and are MERGED at the
/// end (see <see cref="CodeStatsMap.Merge"/>). Counts, presence, types and numeric range merge
/// exactly; the capped value set and the 5 sample values can differ slightly from a single-threaded
/// run when a field is high-cardinality, which is acceptable for these display-only hints.</para>
/// </summary>
public sealed class ShardedPacketStats
{
    private readonly int _workerCount;
    private readonly Channel<List<(string Kind, int Code, ParamSet Params)>>[] _channels;
    private readonly Task<CodeStatsMap>[] _workers;
    private int _next;

    public ShardedPacketStats(int? workerCount = null)
    {
        // Use most of the box for stats; the parse pipeline runs concurrently, so leave a little
        // headroom rather than oversubscribing every core with stats workers alone.
        _workerCount = workerCount ?? Math.Clamp(Environment.ProcessorCount - 1, 4, 16);
        _channels = new Channel<List<(string, int, ParamSet)>>[_workerCount];
        _workers = new Task<CodeStatsMap>[_workerCount];

        for (int i = 0; i < _workerCount; i++)
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

    /// <summary>
    /// Routes one load batch to a worker round-robin. Awaited per batch (bounded channels give
    /// backpressure). Whole batches go to one worker so the per-worker stream stays in file order,
    /// which keeps the merged sample values close to a sequential run.
    /// </summary>
    public async ValueTask FeedAsync(IReadOnlyList<(PacketEntry Entry, ParamSet Params)> items)
    {
        var list = new List<(string, int, ParamSet)>(items.Count);
        foreach (var (entry, ps) in items)
            list.Add((entry.Kind, entry.Code, ps));

        int w = (int)((uint)_next++ % (uint)_workerCount);
        await _channels[w].Writer.WriteAsync(list);
    }

    /// <summary>Completes all workers and returns their maps (overlapping; the caller merges them).</summary>
    public async Task<IReadOnlyList<CodeStatsMap>> CompleteAsync()
    {
        foreach (var c in _channels)
            c.Writer.Complete();
        return await Task.WhenAll(_workers);
    }

    /// <summary>
    /// Abandons the workers after a failed load: completes the channels and observes the tasks so
    /// nothing is left running or unobserved. Safe to call after CompleteAsync too.
    /// </summary>
    public async Task AbortAsync()
    {
        foreach (var c in _channels)
            c.Writer.TryComplete();
        try { await Task.WhenAll(_workers); } catch { /* abandoned results */ }
    }
}
