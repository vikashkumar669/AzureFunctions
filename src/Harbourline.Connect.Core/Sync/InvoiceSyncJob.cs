using System.Globalization;
using Harbourline.Connect.Core.Integrations;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk.Messages;

namespace Harbourline.Connect.Core.Sync;

public sealed record SyncResult(int Upserted, int Failed, DateTimeOffset Watermark);

/// <summary>
/// Pulls invoices changed in LedgerOne since the last run and upserts
/// them into Dataverse (Chapter 10). Safe to run twice: upsert by
/// alternate key makes every write idempotent.
/// </summary>
public sealed partial class InvoiceSyncJob(
    ILedgerOneClient ledger,
    IOrganizationServiceAsync2 dataverse,
    ICheckpointStore checkpoints,
    TimeProvider time,
    ILogger<InvoiceSyncJob> logger)
{
    public const string CheckpointName = "ledgerone-invoices";

    /// <summary>Re-read a little history to cover clock skew.</summary>
    public static readonly TimeSpan Overlap = TimeSpan.FromMinutes(5);

    // <listing:invoice-sync>
    public async Task<SyncResult> RunAsync(
        CancellationToken cancellationToken)
    {
        var checkpoint = await checkpoints
            .GetAsync(CheckpointName, cancellationToken)
            .ConfigureAwait(false);
        var since = checkpoint.Token is null
            ? DateTimeOffset.UnixEpoch
            : DateTimeOffset.Parse(checkpoint.Token,
                CultureInfo.InvariantCulture) - Overlap;   // <1>

        var startedAt = time.GetUtcNow();                  // <2>
        int upserted = 0, failed = 0;
        int? page = 1;

        while (page is { } current)
        {
            var result = await ledger
                .GetInvoicesAsync(since, current, cancellationToken)
                .ConfigureAwait(false);

            foreach (var invoice in result.Items)
            {
                try
                {
                    await dataverse.ExecuteAsync(new UpsertRequest
                    {
                        Target = InvoiceMapper.ToEntity(invoice),
                    }, cancellationToken).ConfigureAwait(false);
                    upserted++;
                }
                catch (Exception ex) when (
                    ex is not OperationCanceledException)
                {
                    failed++;                              // <3>
                    LogInvoiceFailed(logger, ex, invoice.Number);
                }
            }

            page = result.NextPage;
        }

        if (failed == 0)                                   // <4>
        {
            await checkpoints.SaveAsync(new Checkpoint(
                    CheckpointName,
                    startedAt.ToString("O",
                        CultureInfo.InvariantCulture),
                    startedAt.UtcDateTime),
                cancellationToken).ConfigureAwait(false);
        }

        return new SyncResult(upserted, failed, startedAt);
    }
    // </listing:invoice-sync>

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Invoice {InvoiceNumber} could not be synchronised")]
    private static partial void LogInvoiceFailed(
        ILogger logger, Exception exception, string invoiceNumber);
}
