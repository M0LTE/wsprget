namespace wsprget;

internal class BandRunnerFactory(ILogger<Worker> logger, SpotDeduplicator deduplicator, IHttpClientFactory httpClientFactory, Publisher publisher)
{
    private readonly Dictionary<Band, BandRunner> runners = [];

    public BandRunner GetOrCreate(Band band)
    {
        if (!runners.TryGetValue(band, out var bandRunner))
        {
            bandRunner = new BandRunner(band, logger, deduplicator, httpClientFactory, publisher);
            runners[band] = bandRunner;
        }

        return bandRunner;
    }
}
