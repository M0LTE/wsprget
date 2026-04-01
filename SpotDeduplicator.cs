using Microsoft.Extensions.Caching.Distributed;
using System.Collections.Concurrent;

namespace wsprget;

internal sealed class SpotDeduplicator : IDisposable
{
    private readonly ConcurrentDictionary<string, long> _seen = new();
    private readonly IDistributedCache _cache;
    private readonly ILogger<SpotDeduplicator> _logger;
    private readonly Timer _evictionTimer;
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(1);

    public SpotDeduplicator(IDistributedCache cache, ILogger<SpotDeduplicator> logger)
    {
        _cache = cache;
        _logger = logger;
        _evictionTimer = new Timer(EvictExpired, null, TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(15));
    }

    /// <summary>
    /// Check if a spot has been seen. L1 (memory) first, then L2 (Redis) on miss.
    /// </summary>
    public async Task<bool> ExistsAsync(Spot spot)
    {
        if (_seen.ContainsKey(spot.Hash))
            return true;

        if (await _cache.GetAsync(spot.Hash) is not null)
        {
            _seen[spot.Hash] = Environment.TickCount64;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Mark a batch of spots as seen. Writes to L1 immediately, then fires all
    /// Redis SETs concurrently (auto-pipelined by StackExchange.Redis).
    /// </summary>
    public async Task MarkSeenBatchAsync(IReadOnlyList<Spot> spots, CancellationToken ct)
    {
        if (spots.Count == 0) return;

        var now = Environment.TickCount64;
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = Ttl,
        };

        foreach (var spot in spots)
            _seen[spot.Hash] = now;

        var tasks = new Task[spots.Count];
        for (int i = 0; i < spots.Count; i++)
        {
            tasks[i] = _cache.SetAsync(spots[i].Hash, new byte[1], options, ct);
        }
        await Task.WhenAll(tasks);
    }

    private void EvictExpired(object? state)
    {
        var cutoff = Environment.TickCount64 - (long)Ttl.TotalMilliseconds;
        var removed = 0;
        foreach (var kvp in _seen)
        {
            if (kvp.Value < cutoff)
            {
                _seen.TryRemove(kvp.Key, out _);
                removed++;
            }
        }
        if (removed > 0)
            _logger.LogInformation("L1 cache eviction: removed {Count} expired entries, {Remaining} remaining", removed, _seen.Count);
    }

    public void Dispose() => _evictionTimer.Dispose();
}
