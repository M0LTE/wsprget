using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using wsprget;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddSingleton<InstrumentationSource>();
builder.Services.AddHostedService<Worker>();
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = "redis";
    options.InstanceName = "Instance";
});
builder.Services.AddHttpClient("WSPR", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Add("User-Agent", "WSPRGet/1.0");
});
builder.Services.AddSingleton<Publisher>();
builder.Services.AddSingleton<BandRunnerFactory>();
builder.Services.AddLogging(logging =>
{
    logging.ClearProviders();
    logging.AddSimpleConsole(options =>
    {
        options.SingleLine = true;
    });
    logging.SetMinimumLevel(LogLevel.Information);
});

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(serviceName: builder.Environment.ApplicationName))
    .WithMetrics(metrics => metrics
    // Metrics provider from OpenTelemetry
    .AddAspNetCoreInstrumentation()
    .AddMeter(InstrumentationSource.MeterName)
    // Metrics provides by ASP.NET Core in .NET 8
    .AddMeter("Microsoft.AspNetCore.Hosting")
    .AddMeter("Microsoft.AspNetCore.Server.Kestrel")
    // Metrics provided by System.Net libraries
    .AddMeter("System.Net.Http")
    .AddMeter("System.Net.NameResolution")
    .AddPrometheusExporter());
/*otel.WithTracing(tracing =>
{
    tracing.AddAspNetCoreInstrumentation();
    tracing.AddHttpClientInstrumentation();
    tracing.AddSource(InstrumentationSource.ActivitySourceName);
    if (tracingOtlpEndpoint != null)
    {
        tracing.AddOtlpExporter(otlpOptions =>
        {
            otlpOptions.Endpoint = new Uri(tracingOtlpEndpoint);
        });
    }
    else
    {
        tracing.AddConsoleExporter();
    }
});*/

var app = builder.Build();
app.MapOpenApi();
app.MapControllers();
app.MapPrometheusScrapingEndpoint();
app.Run();
