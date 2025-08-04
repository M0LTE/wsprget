using Microsoft.Extensions.Caching.Distributed;

namespace wsprget;

internal class BandRunnerFactory(ILogger<Worker> logger, IDistributedCache cache, IHttpClientFactory httpClientFactory, Publisher publisher)
{
    private readonly Dictionary<Band, BandRunner> runners = [];

    public BandRunner GetOrCreate(Band band)
    {
        if (!runners.TryGetValue(band, out var bandRunner))
        {
            bandRunner = new BandRunner(band, logger, cache, httpClientFactory, publisher);
            runners[band] = bandRunner;
        }

        return bandRunner;
    }
}
