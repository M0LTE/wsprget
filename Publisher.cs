using MQTTnet;
using MQTTnet.Extensions.ManagedClient;
using MQTTnet.Protocol;
using MQTTnet.Client;
using System.Text;
using System.Text.Json;

namespace wsprget;

internal class Publisher : IDisposable
{
    const string host = "44.31.241.66";
    const string topic = "wspr";
    
    private readonly IManagedMqttClient _mqttClient;
    private readonly ILogger<Publisher> _logger;
    private bool _disposed = false;

    public Publisher(ILogger<Publisher> logger)
    {
        _logger = logger;

        var mqttClientOptions = new MqttClientOptionsBuilder()
            .WithTcpServer(host, 1883)
            .WithClientId($"wsprget-{Environment.MachineName}-{Guid.NewGuid():N}"[..23]) // MQTT client ID limit
            .WithCleanSession()
            .WithCredentials("wsprget", Environment.GetEnvironmentVariable("WSPRGET_MQTT_PASSWORD") ?? throw new Exception("WSPRGET_MQTT_PASSWORD not set"))
            .Build();

        var managedMqttClientOptions = new ManagedMqttClientOptionsBuilder()
            .WithClientOptions(mqttClientOptions)
            .WithAutoReconnectDelay(TimeSpan.FromSeconds(5))
            .Build();

        _mqttClient = new MqttFactory().CreateManagedMqttClient();
        
        // Set up event handlers
        _mqttClient.ConnectedAsync += OnConnectedAsync;
        _mqttClient.DisconnectedAsync += OnDisconnectedAsync;
        _mqttClient.ConnectingFailedAsync += OnConnectingFailedAsync;

        // Start the managed client
        _ = Task.Run(async () =>
        {
            try
            {
                await _mqttClient.StartAsync(managedMqttClientOptions);
                _logger.LogInformation("MQTT client started and connecting to {Host}", host);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start MQTT client");
            }
        });
    }

    internal async Task Publish(Spot spot)
    {
        try
        {
            if (_disposed)
            {
                _logger.LogWarning("Attempted to publish to disposed MQTT client");
                return;
            }

            // Serialize the spot to JSON
            var json = JsonSerializer.Serialize(spot, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(Encoding.UTF8.GetBytes(json))
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .WithRetainFlag(false)
                .Build();

            await _mqttClient.EnqueueAsync(message);
            
            _logger.LogDebug("Published spot {Call} to MQTT topic {Topic}", spot.Call, topic);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish spot {Call} to MQTT", spot.Call);
        }
    }

    private Task OnConnectedAsync(MqttClientConnectedEventArgs arg)
    {
        _logger.LogInformation("MQTT client connected to {Host}", host);
        return Task.CompletedTask;
    }

    private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs arg)
    {
        _logger.LogWarning("MQTT client disconnected from {Host}. Reason: {Reason}", host, arg.Reason);
        return Task.CompletedTask;
    }

    private Task OnConnectingFailedAsync(ConnectingFailedEventArgs arg)
    {
        _logger.LogError(arg.Exception, "MQTT client failed to connect to {Host}", host);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            
            try
            {
                _mqttClient?.StopAsync().GetAwaiter().GetResult();
                _mqttClient?.Dispose();
                _logger.LogInformation("MQTT client disposed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing MQTT client");
            }
        }
    }
}
