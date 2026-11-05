using Azure.Core;
using Azure.Messaging.ServiceBus;
using Azure.Monitor.OpenTelemetry.Exporter;
using Azure.Storage.Blobs;
using Harbourline.Connect.Core.Integrations;
using Harbourline.Connect.Core.Telemetry;
using Harbourline.Connect.Functions.Middleware;
using Harbourline.Connect.Functions.Orders;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Harbourline.Connect.Functions;

public static class HostingExtensions
{
    public static FunctionsApplicationBuilder AddHarbourlineFunctions(
        this FunctionsApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var config = builder.Configuration;

        builder.Services.AddHarbourlineIntegrations(config);

        // <listing:otel>
        builder.UseMiddleware<CorrelationMiddleware>();    // <1>

        var otel = builder.Services.AddOpenTelemetry()
            .UseFunctionsWorkerDefaults()                  // <2>
            .WithTracing(t => t
                .AddSource(HarbourlineTelemetry.Name))     // <3>
            .WithMetrics(m => m
                .AddMeter(HarbourlineTelemetry.Name));

        if (!string.IsNullOrEmpty(
            config["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
        {
            otel.UseAzureMonitorExporter();                // <4>
        }
        // </listing:otel>

        // <listing:azure-clients>
        builder.Services.AddSingleton(sp => new ServiceBusClient(
            config["ServiceBus:fullyQualifiedNamespace"],  // <1>
            sp.GetRequiredService<TokenCredential>()));
        builder.Services.AddSingleton(sp => sp
            .GetRequiredService<ServiceBusClient>()
            .CreateSender(OrderFunctions.Queue));          // <2>

        builder.Services.AddSingleton(sp => new BlobServiceClient(
            new Uri(config["Exports:BlobServiceUri"]
                ?? "https://localhost/"),
            sp.GetRequiredService<TokenCredential>()));    // <3>
        // </listing:azure-clients>

        return builder;
    }
}
