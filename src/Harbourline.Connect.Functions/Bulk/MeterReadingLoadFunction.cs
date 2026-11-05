using Harbourline.Connect.Core.Bulk;
using Harbourline.Connect.Core.Dataverse;
using Harbourline.Connect.Core.Telemetry;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;

namespace Harbourline.Connect.Functions.Bulk;

/// <summary>
/// Loads a smart-meter export (Chapter 15). The batch size and degree of
/// parallelism come from app settings so you can tune them without a
/// redeployment, and every run logs its throughput.
/// </summary>
public sealed partial class MeterReadingLoadFunction(
    BulkWriter writer,
    ServiceClient client,
    IConfiguration configuration,
    ILogger<MeterReadingLoadFunction> logger)
{
    // <listing:bulk-function>
    [Function("LoadMeterReadings")]
    public async Task Run(
        [BlobTrigger("meter-readings/{name}",
            Source = BlobTriggerSource.EventGrid,
            Connection = "FeedStorage")] Stream content,
        string name,
        CancellationToken cancellationToken)
    {
        var lines = MeterReadings.ReadCsv(content);
        var rows = lines.Select(MeterReadings.ToEntity).ToList();

        var options = new BulkOptions(
            BatchSize: configuration.GetValue("Bulk:BatchSize", 200),
            Parallelism: configuration.GetValue("Bulk:Parallelism",
                client.RecommendedDegreesOfParallelism));  // <1>

        var result = await writer.UpsertAsync(
            MeterReadingTable.LogicalName, rows,
            r => r.GetAttributeValue<string>(
                MeterReadingTable.ReadingKey),
            options, cancellationToken);

        HarbourlineTelemetry.RecordsWritten.Add(result.Succeeded);
        LogResult(logger, name, rows.Count, result.Succeeded,
            result.Errors.Count, result.Requests,
            options.BatchSize, options.Parallelism,
            result.Elapsed.TotalSeconds, result.RowsPerSecond); // <2>
    }
    // </listing:bulk-function>

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Bulk load {Name}: {Rows} rows, {Succeeded} ok, "
            + "{Failed} failed, {Requests} requests, batch "
            + "{BatchSize}, parallelism {Parallelism}, "
            + "{Seconds:F1} s, {RowsPerSecond:F0} rows/s")]
    private static partial void LogResult(ILogger logger, string name,
        int rows, int succeeded, int failed, int requests,
        int batchSize, int parallelism, double seconds,
        double rowsPerSecond);
}
