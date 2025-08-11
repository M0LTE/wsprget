using System.Diagnostics.Metrics;

namespace wsprget;

public class MetricsService : IDisposable
{
    private readonly Meter _meter;
    private Func<int>? _getPendingMessages;
    
    public MetricsService()
    {
        _meter = new Meter("wsprget", "1.0.0");
        
        _meter.CreateObservableGauge<int>(
            "mqtt_pending_messages",
            () => _getPendingMessages?.Invoke() ?? 0,
            "messages",
            "Number of pending MQTT messages in the queue");
    }

    public void SetPendingMessagesCallback(Func<int> getPendingMessages)
    {
        _getPendingMessages = getPendingMessages;
    }

    public void Dispose()
    {
        _meter.Dispose();
    }
}