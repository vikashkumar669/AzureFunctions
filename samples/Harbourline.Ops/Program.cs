using System.Globalization;
using Azure.Identity;
using CsvHelper;
using CsvHelper.Configuration;
using Harbourline.Connect.Core.Bulk;
using Harbourline.Connect.Core.Dataverse;
using Harbourline.Connect.Core.Migration;
using Harbourline.Connect.Core.Sync;
using Harbourline.Ops;
using Microsoft.Extensions.Logging;

var credential = new DefaultAzureCredential();
using var logs = LoggerFactory.Create(b => b.AddSimpleConsole());

switch (args.FirstOrDefault())
{
    case "dlq-peek" when args.Length >= 3:
        await DeadLetterTools.PeekAsync(args[1], args[2], credential);
        break;

    case "dlq-replay" when args.Length >= 3:
        await DeadLetterTools.ReplayAsync(args[1], args[2],
            args.Length > 3 ? args[3] : null, credential);
        break;

    case "migrate" when args.Length >= 3:
    {
        using var client = Connect();
        var runner = new MigrationRunner(
            new BulkWriter(client, logs.CreateLogger<BulkWriter>()),
            new DataverseCheckpointStore(client),
            TimeProvider.System,
            logs.CreateLogger<MigrationRunner>());
        var report = await runner.RunAsync(
            args[2], ReadLegacy(args[1]), CancellationToken.None);
        Console.WriteLine($"Source rows: {report.SourceRows}");
        Console.WriteLine($"Migrated:    {report.Migrated}");
        Console.WriteLine($"Resumed after batch: {report.SkippedBatches}");
        foreach (var (code, error) in report.Errors)
        {
            Console.WriteLine($"  {code}: {error}");
        }

        break;
    }

    case "reconcile" when args.Length >= 2:
    {
        using var client = Connect();
        var expected = ReadLegacy(args[1]).Select(LegacyMapping.Map)
            .Where(m => m.Row is not null).Select(m => m.Row!).ToList();
        var report = await new Reconciler(client)
            .ReconcileAsync(expected, CancellationToken.None);
        Console.WriteLine($"Source {report.SourceCount}, " +
            $"target {report.TargetCount}");
        Console.WriteLine($"Missing:   {report.MissingInTarget.Count}");
        Console.WriteLine($"Different: {report.Different.Count}");
        Console.WriteLine($"Checksums: {report.SourceChecksum[..12]} / " +
            $"{report.TargetChecksum[..12]}");
        Console.WriteLine(report.Matches ? "MATCH" : "MISMATCH");
        break;
    }

    default:
        Console.WriteLine("Usage: migrate|reconcile|dlq-peek|dlq-replay …");
        break;
}

Microsoft.PowerPlatform.Dataverse.Client.ServiceClient Connect() =>
    DataverseClientFactory.Create(
        new DataverseOptions
        {
            Url = Environment.GetEnvironmentVariable("Dataverse__Url")
                ?? throw new InvalidOperationException(
                    "Set Dataverse__Url first."),
        },
        credential, logs.CreateLogger("Dataverse"));

static List<LegacyPropertyRow> ReadLegacy(string path)
{
    var config = new CsvConfiguration(CultureInfo.InvariantCulture)
    {
        // Legacy headers look like PROP_REF; match them to PropRef.
        PrepareHeaderForMatch = a => a.Header
            .Replace("_", "", StringComparison.Ordinal)
            .ToUpperInvariant(),
    };
    using var reader = new StreamReader(path);
    using var csv = new CsvReader(reader, config);
    return csv.GetRecords<LegacyPropertyRow>().ToList();
}
