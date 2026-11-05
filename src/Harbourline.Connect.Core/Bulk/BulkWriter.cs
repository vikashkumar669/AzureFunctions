using System.Collections.Concurrent;
using System.Diagnostics;
using Harbourline.Connect.Core.Dataverse;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;

namespace Harbourline.Connect.Core.Bulk;

public sealed record BulkOptions(
    int BatchSize = 200,
    int Parallelism = 4)
{
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(BatchSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(BatchSize, 1000);
        ArgumentOutOfRangeException.ThrowIfLessThan(Parallelism, 1);
    }
}

/// <summary>A row that Dataverse rejected, with the reason.</summary>
public sealed record RowError(int Index, string Key, string Message);

public sealed record BulkResult(
    int Succeeded,
    IReadOnlyList<RowError> Errors,
    int Requests,
    TimeSpan Elapsed)
{
    public double RowsPerSecond => Elapsed.TotalSeconds > 0
        ? Succeeded / Elapsed.TotalSeconds
        : 0;
}

/// <summary>
/// Writes many rows with UpsertMultiple, in parallel batches. On a
/// standard table a batch is all-or-nothing, so a failed batch is split
/// in half until the bad rows are isolated (Chapter 15).
/// </summary>
public sealed partial class BulkWriter(
    IOrganizationServiceAsync2 dataverse,
    ILogger<BulkWriter> logger)
{
    // <listing:bulk-upsert>
    public async Task<BulkResult> UpsertAsync(
        string table,
        IReadOnlyList<Entity> rows,
        Func<Entity, string> keyOf,
        BulkOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        var stopwatch = Stopwatch.StartNew();
        var errors = new ConcurrentBag<RowError>();
        int succeeded = 0, requests = 0;

        var batches = Enumerable.Range(0, rows.Count)      // <1>
            .Chunk(options.BatchSize);

        await Parallel.ForEachAsync(batches,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = options.Parallelism, // <2>
                CancellationToken = cancellationToken,
            },
            async (indexes, ct) =>
            {
                var (ok, sent) = await WriteAsync(
                    table, rows, indexes, keyOf, errors, ct)
                    .ConfigureAwait(false);
                Interlocked.Add(ref succeeded, ok);
                Interlocked.Add(ref requests, sent);
            }).ConfigureAwait(false);

        return new BulkResult(succeeded,
            errors.OrderBy(e => e.Index).ToList(),
            requests, stopwatch.Elapsed);
    }
    // </listing:bulk-upsert>

    // <listing:bulk-split>
    private async Task<(int Ok, int Requests)> WriteAsync(
        string table,
        IReadOnlyList<Entity> rows,
        int[] indexes,
        Func<Entity, string> keyOf,
        ConcurrentBag<RowError> errors,
        CancellationToken ct)
    {
        var targets = new EntityCollection(
            indexes.Select(i => rows[i]).ToList())
        {
            EntityName = table,                            // <1>
        };

        try
        {
            await dataverse.ExecuteAsync(
                new UpsertMultipleRequest { Targets = targets }, ct)
                .ConfigureAwait(false);
            return (indexes.Length, 1);
        }
        catch (Exception ex) when (
            ex is not OperationCanceledException
            && DataverseErrors.Classify(ex)?.Kind
                is not DataverseErrorKind.Throttled)       // <2>
        {
            if (indexes.Length == 1)                       // <3>
            {
                var i = indexes[0];
                errors.Add(new RowError(i, keyOf(rows[i]),
                    DataverseErrors.Classify(ex)?.Message
                        ?? ex.Message));
                return (0, 1);
            }

            LogSplitting(logger, indexes.Length);
            var half = indexes.Length / 2;                 // <4>
            var left = await WriteAsync(table, rows,
                indexes[..half], keyOf, errors, ct)
                .ConfigureAwait(false);
            var right = await WriteAsync(table, rows,
                indexes[half..], keyOf, errors, ct)
                .ConfigureAwait(false);
            return (left.Ok + right.Ok,
                1 + left.Requests + right.Requests);
        }
    }
    // </listing:bulk-split>

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Batch of {Count} rows failed; splitting to find bad rows")]
    private static partial void LogSplitting(ILogger logger, int count);
}
