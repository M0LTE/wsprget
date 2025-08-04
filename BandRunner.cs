using Microsoft.Extensions.Caching.Distributed;
using System.Diagnostics;

namespace wsprget;

internal class BandRunner(Band band, ILogger<Worker> _logger, IDistributedCache _cache, IHttpClientFactory _httpClientFactory)
{
    private readonly Stopwatch timerSinceLastRequest = new();
    private const int minSecsBetweenRequests = 5;
    private const int maxAgeDays = 7;
    private int limit = 1000;
    private const int maxLimit = 2000;

    public async Task<bool> OneShot(bool skipDelay, CancellationToken stoppingToken)
    {
        try
        {
            if (!timerSinceLastRequest.IsRunning)
            {
                timerSinceLastRequest.Start();
            }
            else
            {
                if (!skipDelay)
                {
                    if (timerSinceLastRequest.Elapsed < TimeSpan.FromSeconds(minSecsBetweenRequests))
                    {
                        var delay = TimeSpan.FromSeconds(minSecsBetweenRequests) - timerSinceLastRequest.Elapsed;
                        if (delay > TimeSpan.Zero)
                        {
                            _logger.LogInformation("{band}: Waiting {delay} before next request", GetBand(band), delay);
                            await Task.Delay(delay, stoppingToken);
                        }
                    }
                    else
                    {
                         _logger.LogInformation("{band}: Skipping delay as enough time has passed since last request", GetBand(band));
                    }
                }
            }

            var rawNewSpots = await GetSpots(band, limit, stoppingToken);
            timerSinceLastRequest.Restart();
            var timeFilteredNewSpots = TimeFilter(rawNewSpots).ToArray();

            foreach (var spot in timeFilteredNewSpots)
            {
                var options = new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(maxAgeDays),
                };
                await _cache.SetAsync(spot.Hash, new byte[1], options, stoppingToken);
            }

            _logger.LogInformation("{band}: {count} new spots, limit was {limit}", GetBand(band), timeFilteredNewSpots.Length, limit);

            limit = CalculateNewLimit(timeFilteredNewSpots.Length);

            if (!skipDelay && timeFilteredNewSpots.Length < 5)
            {
                _logger.LogInformation("{band}: Small number of spots, sleeping for 30 seconds", GetBand(band));
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }

            if (rawNewSpots.Count == limit)
            {
                Debugger.Break();
                return true; // Indicate that we should skip the delay next time
            }
            else
            {
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("{band}: Error fetching spots: {exception}", GetBand(band), ex.Message);
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            return false;
        }
    }

    private static string GetBand(Band band) => band switch
    {
        Band.M2190_600 => "2190/630m",
        Band.M220 => "1.25m",
        Band.M432 => "70cm",
        Band.GHz => "uwave",
        _ => band.ToString()[1..] + "m",
    };

    private int CalculateNewLimit(int newSpotsThisCycle)
    {
        var newLimit = limit;
        
        if (newLimit > newSpotsThisCycle * 3)
        {
            newLimit = (int)(0.8 * newLimit);
            _logger.LogInformation("{band}: Reducing limit from {limit} to {newLimit} based on {newSpots} new spots", GetBand(band), limit, newLimit, newSpotsThisCycle);
        }
        else if (newLimit < newSpotsThisCycle * 2)
        {
            newLimit *= 2;
            _logger.LogInformation("{band}: Increasing limit from {limit} to {newLimit} based on {newSpots} new spots", GetBand(band), limit, newLimit, newSpotsThisCycle);
        }

        if (newLimit < 100)
        {
            newLimit = 100;
            _logger.LogInformation("{band}: Clamping limit to {newLimit} as it was below 100", GetBand(band), newLimit);
        }
        else if (newLimit > maxLimit)
        {
            newLimit = maxLimit;
            _logger.LogWarning("{band}: Clamping limit to {newLimit} as it was above {maxLimit}", GetBand(band), newLimit, maxLimit);
        }

        return newLimit;
    }

    private static IEnumerable<Spot> TimeFilter(IEnumerable<Spot> spots)
    {
        foreach (var spot in spots)
        {
            if (spot.Timestamp > DateTime.UtcNow.AddDays(-maxAgeDays) && spot.Timestamp < DateTime.UtcNow.AddMinutes(10))
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
        
        using var httpClient = _httpClientFactory.CreateClient("WSPR");
        var url = $"https://www.wsprnet.org/olddb?mode=html&band={bandCode}&limit={limit}&findcall=&findreporter=&sort=date";
        var sw = Stopwatch.StartNew();
        var response = await httpClient.GetAsync(url, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            sw.Stop();

            return await SpotResultsParser.ParseSpots(content, IsInCache, cancellationToken);
        }
        else
        {
            throw new HttpRequestException($"Failed to fetch data from {url}. Status code: {response.StatusCode}");
        }
    }

    private async Task<bool> IsInCache(Spot spot) => await _cache.GetAsync(spot.Hash.ToString()) is not null;
}
