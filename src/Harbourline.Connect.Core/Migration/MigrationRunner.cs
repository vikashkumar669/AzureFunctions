using System.Globalization;
using Harbourline.Connect.Core.Bulk;
using Harbourline.Connect.Core.Dataverse;
using Harbourline.Connect.Core.Sync;
using Microsoft.Extensions.Logging;

namespace Harbourline.Connect.Core.Migration;

public sealed record MigrationReport(
    int SourceRows,
    int Migrated,
    int SkippedBatches,
    IReadOnlyList<(string Code, string Error)> Errors);

/// <summary>
/// Migrates legacy property rows in numbered batches. After each batch
/// it saves a checkpoint, so a stopped run resumes where it stopped
/// instead of starting again (Chapter 16).
/// </summary>
public sealed partial class MigrationRunner(
    BulkWriter writer,
    ICheckpointStore checkpoints,
    TimeProvider time,
    ILogger<MigrationRunner> logger)
{
    public const int BatchSize = 500;

    // <listing:migration-run>
    public async Task<MigrationReport> RunAsync(
        string runName,
        IReadOnlyList<LegacyPropertyRow> source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        var checkpoint = await checkpoints
            .GetAsync(runName, cancellationToken)
            .ConfigureAwait(false);
        var done = int.TryParse(checkpoint.Token,
            CultureInfo.InvariantCulture, out var n) ? n : 0;  // <1>

        var errors = new List<(string, string)>();
        int migrated = 0, batch = 0;

        foreach (var chunk in source.Chunk(BatchSize))     // <2>
        {
            batch++;
            if (batch <= done)
            {
                continue;                                  // <3>
            }

            var mapped = chunk.Select(LegacyMapping.Map).ToList();
            errors.AddRange(mapped.Where(m => m.Error is not null)
                .Select(m => (m.Code, m.Error!)));
            var valid = mapped.Where(m => m.Row is not null).ToList();

            var result = await writer.UpsertAsync(
                PropertyTable.LogicalName,
                valid.Select(m => m.Row!).ToList(),
                r => r.GetAttributeValue<string>(PropertyTable.Code),
                new BulkOptions(BatchSize: 250, Parallelism: 4),
                cancellationToken).ConfigureAwait(false);

            migrated += result.Succeeded;
            errors.AddRange(result.Errors
                .Select(e => (e.Key, e.Message)));

            await checkpoints.SaveAsync(new Checkpoint(    // <4>
                    runName,
                    batch.ToString(CultureInfo.InvariantCulture),
                    time.GetUtcNow().UtcDateTime),
                cancellationToken).ConfigureAwait(false);
            LogBatch(logger, batch, result.Succeeded);
        }

        return new MigrationReport(
            source.Count, migrated, done, errors);
    }
    // </listing:migration-run>

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Migration batch {Batch} done: {Rows} rows")]
    private static partial void LogBatch(
        ILogger logger, int batch, int rows);
}
