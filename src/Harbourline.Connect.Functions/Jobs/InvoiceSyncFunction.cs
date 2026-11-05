using Harbourline.Connect.Core.Sync;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Harbourline.Connect.Functions.Jobs;

/// <summary>Nightly LedgerOne → Dataverse invoice sync (Chapter 10).</summary>
public sealed partial class InvoiceSyncFunction(
    InvoiceSyncJob job, ILogger<InvoiceSyncFunction> logger)
{
    // <listing:timer-function>
    [Function("NightlyInvoiceSync")]
    public async Task Run(
        [TimerTrigger("%InvoiceSyncSchedule%")]           // <1>
        TimerInfo timer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(timer);
        if (timer.IsPastDue)                               // <2>
        {
            LogPastDue(logger);
        }

        var result = await job.RunAsync(cancellationToken);
        LogDone(logger, result.Upserted, result.Failed,
            result.Watermark);

        if (result.Failed > 0)
        {
            throw new InvalidOperationException(           // <3>
                $"{result.Failed} invoices failed; the watermark " +
                "was not advanced and the next run retries them.");
        }
    }
    // </listing:timer-function>

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Invoice sync is running late")]
    private static partial void LogPastDue(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Invoice sync: {Upserted} upserted, {Failed} failed, "
            + "watermark {Watermark}")]
    private static partial void LogDone(ILogger logger, int upserted,
        int failed, DateTimeOffset watermark);
}
