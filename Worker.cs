namespace wsprget;

internal class Worker(BandRunnerFactory bandRunnerFactory) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var tasks = new List<Task>();
        //var bands = new List<Band> { Band.M40 };
        var bands = Enum.GetValues<Band>();

        foreach (var band in bands)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            var bandRunner = bandRunnerFactory.GetOrCreate(band);
            var bandLoop = RunLoop(bandRunner, stoppingToken);
            tasks.Add(bandLoop);
        }

        await Task.WhenAll(tasks);
    }

    internal static async Task RunLoop(BandRunner runner, CancellationToken stoppingToken)
    {
        await Task.Delay(Random.Shared.Next(0, 5000), stoppingToken);

        bool skipDelay = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            skipDelay = await runner.OneShot(skipDelay, stoppingToken);
        }
    }
}
