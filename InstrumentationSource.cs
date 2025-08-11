namespace wsprget;

using System.Diagnostics.Metrics;

public sealed class InstrumentationSource : IDisposable
{
    //internal const string ActivitySourceName = "wsprget";
    internal const string MeterName = "wsprget";
    private readonly Meter meter;

    public InstrumentationSource()
    {
        string? version = typeof(InstrumentationSource).Assembly.GetName().Version?.ToString();
        //this.ActivitySource = new ActivitySource(ActivitySourceName, version);
        this.meter = new Meter(MeterName, version);
        this.SpotsQueuedForPublishCounter = this.meter.CreateCounter<long>("spots.wspr.queuedforpublish", description: "The number of WSPR spots queued for publishing since startup");
    }

    //public ActivitySource ActivitySource { get; }

    public Counter<long> SpotsQueuedForPublishCounter { get; }

    public void Dispose()
    {
        //this.ActivitySource.Dispose();
        this.meter.Dispose();
    }
}