using Azure.Messaging.ServiceBus;
using Harbourline.Connect.Core.Dataverse;
using Harbourline.Connect.Core.Events;
using Harbourline.Connect.Core.Integrations;
using Harbourline.Connect.Core.Orders;
using Harbourline.Connect.Core.Telemetry;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Harbourline.Connect.Functions.Messaging;

/// <summary>
/// Sends account changes to LedgerOne (Chapter 11). Dataverse posts
/// each Create/Update of account to the account-events topic; this
/// function reads the "ledgerone" subscription.
/// </summary>
public sealed partial class AccountEventsFunction(
    ILedgerOneClient ledger,
    TimeProvider time,
    ILogger<AccountEventsFunction> logger)
{
    // <listing:sb-function>
    [Function("AccountEventsToLedgerOne")]
    public async Task Run(
        [ServiceBusTrigger("account-events", "ledgerone",
            Connection = "ServiceBus",
            AutoCompleteMessages = false)]                 // <1>
        ServiceBusReceivedMessage message,
        ServiceBusMessageActions actions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(actions);

        try
        {
            var evt = DataverseEvent.Parse(message.Body.ToString());
            var customerId = evt.GetString(AccountTable.AccountNumber);
            if (customerId is null)
            {
                await actions.CompleteMessageAsync(        // <2>
                    message, cancellationToken);
                return;
            }

            await ledger.UpsertCustomerAsync(
                new LedgerCustomer(customerId,
                    evt.GetString(AccountTable.Name) ?? "",
                    evt.GetString(AccountTable.Email),
                    evt.GetString(AccountTable.Phone),
                    evt.GetString(AccountTable.City),
                    time.GetUtcNow()),
                idempotencyKey: message.MessageId,         // <3>
                cancellationToken);

            await actions.CompleteMessageAsync(
                message, cancellationToken);
        }
        catch (Exception ex) when (
            !FailureClassifier.IsTransient(ex)
            || message.DeliveryCount >= MaxAttempts)       // <4>
        {
            LogDeadLetter(logger, ex, message.MessageId);
            HarbourlineTelemetry.MessagesDeadLettered.Add(1);
            await actions.DeadLetterMessageAsync(message,
                deadLetterReason: ex.GetType().Name,
                deadLetterErrorDescription: Truncate(ex.Message),
                cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (
            FailureClassifier.IsTransient(ex))
        {
            await actions.AbandonMessageAsync(             // <5>
                message, cancellationToken: cancellationToken);
            throw;
        }
    }
    // </listing:sb-function>

    /// <summary>Matches the subscription's max delivery count.</summary>
    public const int MaxAttempts = 10;

    private static string Truncate(string s) =>
        s.Length <= 1000 ? s : s[..1000];

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Dead-lettering message {MessageId}")]
    private static partial void LogDeadLetter(
        ILogger logger, Exception exception, string messageId);
}
