using Harbourline.Connect.Core.Customers;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Harbourline.Connect.Functions.Jobs;

/// <summary>Customer sync in both directions, every five minutes (Chapter 21).</summary>
public sealed partial class CustomerSyncFunctions(
    CustomerSyncService sync, ILogger<CustomerSyncFunctions> logger)
{
    // <listing:sync-timers>
    [Function("PushCustomersToLedgerOne")]
    public async Task Push(
        [TimerTrigger("0 */5 * * * *")] TimerInfo timer,   // <1>
        CancellationToken cancellationToken)
    {
        var pushed = await sync.PushAsync(cancellationToken);
        LogSync(logger, "Dataverse → LedgerOne", pushed);
    }

    [Function("PullCustomersFromLedgerOne")]
    public async Task Pull(
        [TimerTrigger("30 */5 * * * *")] TimerInfo timer,  // <2>
        CancellationToken cancellationToken)
    {
        var pulled = await sync.PullAsync(cancellationToken);
        LogSync(logger, "LedgerOne → Dataverse", pulled);
    }
    // </listing:sync-timers>

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Customer sync {Direction}: {Count} changed")]
    private static partial void LogSync(
        ILogger logger, string direction, int count);
}
