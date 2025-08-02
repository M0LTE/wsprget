using Microsoft.Extensions.Caching.Distributed;
using System.Diagnostics;

namespace wsprget;

public partial class Worker(ILogger<Worker> _logger, IDistributedCache _cache, IHttpClientFactory _httpClientFactory) : BackgroundService
{
    private const int cycleTime = 30; // seconds
    private bool parallel = false;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var tasks = new List<Task>();

        if (parallel)
        {
            foreach (var band in Enum.GetValues<Band>())
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    return;
                }

                _ = RunLoop(band, stoppingToken);
            }

            await Task.WhenAll(tasks);
        }
        else
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                foreach (var band in Enum.GetValues<Band>())
                {
                    var sw = Stopwatch.StartNew();
                    await OneShot(band, stoppingToken);
                    sw.Stop();
                    if (sw.ElapsedMilliseconds < 2000)
                    {
                        await Task.Delay(2000 - (int)sw.ElapsedMilliseconds, stoppingToken);
                    }
                }
            }
        }
    }

    private static readonly Random random = new();
    private Dictionary<Band, int> lastCounts = [];

    private async Task RunLoop(Band band, CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(random.Next(0, cycleTime)), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await OneShot(band, stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(cycleTime), stoppingToken);
        }
    }

    private async Task OneShot(Band band, CancellationToken stoppingToken)
    {
        if (!lastCounts.TryGetValue(band, out int lastCount))
        {
            lastCount = 2000;
            lastCounts[band] = lastCount;
        }

        try
        {
            var lim = lastCount == 0 ? 10 : lastCount * 2;
            var rawSpots = await GetSpots(band, lim, stoppingToken);
            var timeFilteredSpots = TimeFilter(rawSpots);
            int count = 0;
            int uncached = 0;
            foreach (var spot in timeFilteredSpots)
            {
                count++;
                if (stoppingToken.IsCancellationRequested)
                {
                    return;
                }

                var hash = spot.GetHash().ToString();

                var item = await _cache.GetAsync(hash, stoppingToken);

                if (item is null)
                {
                    var options = new DistributedCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(7),
                    };
                    await _cache.SetAsync(hash, new byte[1], options, stoppingToken);
                    uncached++;
                }
            }

            if (uncached > 0)
            {
                _logger.LogInformation("Found {new} new spots for band {Band}, limit was {limit}", uncached, band, lim);
            }

            lastCounts[band] = uncached;
        }
        catch (Exception ex)
        {
            _logger.LogError("Error fetching spots for band {Band}: {exception}", band, ex.Message);
            await Task.Delay(TimeSpan.FromSeconds(cycleTime), stoppingToken);
        }
    }

    private static IEnumerable<Spot> TimeFilter(IEnumerable<Spot> spots)
    {
        foreach (var spot in spots)
        {
            if (spot.Timestamp > DateTime.UtcNow.AddDays(-7) && spot.Timestamp < DateTime.UtcNow.AddDays(1))
            {
                yield return spot;
            }
        }
    }

    private readonly static Dictionary<Band, string> BandNames = new()
    {
        { Band.M2190_600, "2190" },
        { Band.M160, "160" },
        { Band.M80, "80" },
        { Band.M60, "60" },
        { Band.M40, "40" },
        { Band.M30, "30" },
        { Band.M20, "20" },
        { Band.M17, "17" },
        { Band.M15, "15" },
        { Band.M12, "12" },
        { Band.M10, "10" },
        { Band.M8, "8" },
        { Band.M6, "6" },
        { Band.M4, "4" },
        { Band.M2, "2" },
        { Band.M220, "220" },
        { Band.M432, "432" },
        { Band.GHz, "u" }
    };

    private async Task<List<Spot>> GetSpots(Band band, int limit, CancellationToken cancellationToken)
    {
        // https://www.wsprnet.org/olddb?mode=html&band=6&limit=10000&findcall=&findreporter=&sort=date

        if (!BandNames.TryGetValue(band, out var bandCode))
        {
            throw new ArgumentException($"Invalid band: {band}", nameof(band));
        }

        if (limit < 100)
        {
            limit = 100;
        }
        else if (limit > 10000)
        {
            limit = 10000;
        }

        using var httpClient = _httpClientFactory.CreateClient("WSPR");
        var url = $"https://www.wsprnet.org/olddb?mode=html&band={bandCode}&limit={limit}&findcall=&findreporter=&sort=date";
        var sw = Stopwatch.StartNew();
        var response = await httpClient.GetAsync(url, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            sw.Stop();
            //_logger.LogInformation("Fetched {band} spots limit {limit} in {elapsed:0} ms", band, limit, sw.ElapsedMilliseconds);

            return SpotResultsParser.ParseSpots(content, cancellationToken);
        }
        else
        {
            throw new HttpRequestException($"Failed to fetch data from {url}. Status code: {response.StatusCode}");
        }
    }
}
