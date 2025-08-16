namespace wsprget;

internal class Worker(BandRunnerFactory bandRunnerFactory, ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var tasks = new List<Task>();
        //var bands = new List<Band> { Band.M8 };
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

    internal async Task RunLoop(BandRunner runner, CancellationToken stoppingToken)
    {
        var wait = Random.Shared.Next(0, Constants.MillisecondsBetweenRequests);
        await Task.Delay(wait, stoppingToken);
        logger.LogInformation("Waited {wait}ms before starting {band} runner", wait, runner.BandName);

        while (!stoppingToken.IsCancellationRequested)
        {
            await runner.OneShot(stoppingToken);
        }
    }
}

internal class Constants
{
    public const int MillisecondsBetweenRequests = 10000;
}