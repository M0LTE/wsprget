using AmateurBandLib;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;
using MQTTnet.Protocol;
using System.Text;
using System.Text.Json;

namespace wsprget;

internal class Publisher : IDisposable
{
    const string host = "44.31.241.66";
    const int numberOfClients = 4; // Number of MQTT clients to create

    private readonly IManagedMqttClient[] _mqttClients = new IManagedMqttClient[numberOfClients];
    private readonly ILogger<Publisher> _logger;
    private readonly InstrumentationSource _instrumentationSource;
    private bool _disposed = false;

    public Publisher(ILogger<Publisher> logger, InstrumentationSource instrumentationSource)
    {
        _logger = logger;
        this._instrumentationSource = instrumentationSource;
        var mqttClientOptions = new MqttClientOptionsBuilder()
            .WithTcpServer(host, 1883)
            .WithClientId($"wsprget-{Environment.MachineName}-{Guid.NewGuid():N}"[..23]) // MQTT client ID limit
            .WithCleanSession()
            .WithCredentials("wsprget", Environment.GetEnvironmentVariable("WSPRGET_MQTT_PASSWORD") ?? throw new Exception("WSPRGET_MQTT_PASSWORD not set"))
            .Build();

        var managedMqttClientOptions = new ManagedMqttClientOptionsBuilder()
            .WithClientOptions(mqttClientOptions)
            .WithAutoReconnectDelay(TimeSpan.FromSeconds(5))
            .WithPendingMessagesOverflowStrategy(MQTTnet.Server.MqttPendingMessagesOverflowStrategy.DropOldestQueuedMessage)
            .WithMaxPendingMessages(10000)
            .Build();

        for (int i = 0; i < _mqttClients.Length; i++)
        {
            var _mqttClient = new MqttFactory().CreateManagedMqttClient();

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

            _mqttClients[i] = _mqttClient;
        }
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

            var topic = BuildTopic(spot);
            if (topic == null)
            {
                return;
            }

            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(Encoding.UTF8.GetBytes(json))
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .WithRetainFlag(false)
                .Build();

            var (client, id) = GetRandomClient();
            await client.EnqueueAsync(message);
            _instrumentationSource.SpotsQueuedForPublishCounter.Add(1);
            _instrumentationSource.MqttPublishBacklogLength.Record(client.PendingApplicationMessagesCount, new KeyValuePair<string, object?>("mqttclientid", id));

            _logger.LogDebug("Published spot {Call} to MQTT topic {Topic}", spot.Call, topic);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish spot {Call} to MQTT", spot.Call);
        }
    }

    private (IManagedMqttClient, int) GetRandomClient()
    {
        // Return a random MQTT client from the pool
        var randomIndex = Random.Shared.Next(_mqttClients.Length);
        return (_mqttClients[randomIndex], randomIndex);
    }

    private static string? BuildTopic(Spot spot)
    {
        // wspr/band/mode/sendercall/receipvercall/sendergrid/receivergrid

        var band = AmateurBand.FromHz(spot.FrequencyHz);
        if (string.IsNullOrWhiteSpace(band?.Name))
        {
            return null; // Invalid band
        }

        if (string.IsNullOrWhiteSpace(spot.Mode))
        {
            return null; // Invalid mode
        }

        if (string.IsNullOrWhiteSpace(spot.Call) || string.IsNullOrWhiteSpace(spot.Reporter))
        {
            return null; // Invalid callsigns
        }

        if (string.IsNullOrWhiteSpace(spot.Grid) || string.IsNullOrWhiteSpace(spot.ReporterGrid))
        {
            return null; // Invalid grids
        }

        if (spot.Grid.Length < 4 || spot.ReporterGrid.Length < 4)
        {
            return null; // Grids must be at least 4 characters long
        }

        return $"wspr/{band.Name}/{spot.Mode.ToLowerInvariant()}/{spot.Call.ToUpperInvariant()}/{spot.Reporter.ToUpperInvariant()}/{AtMost(spot.Grid.ToUpperInvariant(), 4)}/{AtMost(spot.ReporterGrid.ToUpperInvariant(), 4)}";
    }

    private static string AtMost(string v1, int v2)
    {
        if (v1.Length > v2)
        {
            return v1[..v2]; // Truncate to the first v2 characters
        }

        return v1;
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
                foreach (var client in _mqttClients)
                {
                    client?.StopAsync().GetAwaiter().GetResult();
                    client?.Dispose();
                }
                _logger.LogInformation("MQTT clients disposed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing MQTT clients");
            }
        }
    }
}
