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
        meter = new Meter(MeterName, version);
        SpotsQueuedForPublishCounter = this.meter.CreateCounter<long>("spots.wspr.mqtt.publish.enqueued", description: "The number of WSPR spots queued for publishing since startup");
        MqttPublishBacklogLength = this.meter.CreateGauge<int>("spots.wspr.mqtt.publish.backlog", description: "The current backlog of spots waiting to be sent to the MQTT server");
    }

    //public ActivitySource ActivitySource { get; }

    public Counter<long> SpotsQueuedForPublishCounter { get; }
    public Gauge<int> MqttPublishBacklogLength { get; }

    public void Dispose()
    {
        //this.ActivitySource.Dispose();
        this.meter.Dispose();
    }
}