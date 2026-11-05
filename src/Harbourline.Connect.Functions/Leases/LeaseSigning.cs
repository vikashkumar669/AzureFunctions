using Harbourline.Connect.Core.Dataverse;
using Harbourline.Connect.Core.Integrations;
using Harbourline.Connect.Core.Leases;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace Harbourline.Connect.Functions.Leases;

public sealed record SignatureResult(string EnvelopeId, string Status);

/// <summary>
/// The lease-signing workflow of Chapter 12, as Durable Functions:
/// draft → envelope sent → wait (with reminders) → activate or decline.
/// </summary>
public static class LeaseSigningOrchestration
{
    public const string SignedEvent = "SignatureCompleted";
    public static readonly TimeSpan ReminderEvery = TimeSpan.FromDays(3);
    public const int MaxReminders = 2;

    public static string InstanceIdFor(string leaseNumber) =>
        "lease-signing-" + leaseNumber;

    // <listing:orchestrator>
    [Function(nameof(LeaseSigningOrchestration))]
    public static async Task<string> Run(
        [OrchestrationTrigger] TaskOrchestrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var leaseNumber = context.GetInput<string>()!;     // <1>
        var retry = TaskOptions.FromRetryPolicy(new RetryPolicy(
            maxNumberOfAttempts: 5,
            firstRetryInterval: TimeSpan.FromSeconds(10),
            backoffCoefficient: 2));                       // <2>

        var envelopeId = await context.CallActivityAsync<string>(
            nameof(LeaseSigningActivities.SendEnvelope),
            leaseNumber, retry);

        for (var reminder = 0; ; reminder++)
        {
            using var timeout = new CancellationTokenSource();
            var signed = context.WaitForExternalEvent<SignatureResult>(
                SignedEvent);                              // <3>
            var due = context.CreateTimer(
                context.CurrentUtcDateTime.Add(ReminderEvery),
                timeout.Token);                            // <4>

            if (await Task.WhenAny(signed, due) == signed)
            {
                timeout.Cancel();                          // <5>
                var status = signed.Result.Status == "Signed"
                    ? LeaseStatus.Active
                    : LeaseStatus.Declined;
                await context.CallActivityAsync(
                    nameof(LeaseSigningActivities.SetStatus),
                    new StatusChange(leaseNumber, status), retry);
                return status.ToString();
            }

            if (reminder == MaxReminders)                  // <6>
            {
                await context.CallActivityAsync(
                    nameof(LeaseSigningActivities.SetStatus),
                    new StatusChange(leaseNumber, LeaseStatus.Draft),
                    retry);
                return "Expired";
            }

            await context.CallActivityAsync(
                nameof(LeaseSigningActivities.SendReminder),
                envelopeId, retry);
        }
    }
    // </listing:orchestrator>
}

public sealed record StatusChange(string LeaseNumber, LeaseStatus Status);

/// <summary>Activities: the only places that call Dataverse or SignBridge.</summary>
public sealed class LeaseSigningActivities(
    LeaseService leases, ISignBridgeClient signBridge)
{
    // <listing:activity>
    [Function(nameof(SendEnvelope))]
    public async Task<string> SendEnvelope(
        [ActivityTrigger] string leaseNumber,
        CancellationToken cancellationToken)
    {
        var lease = await leases.GetSigningDetailsAsync(
            leaseNumber, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Lease {leaseNumber} was not found.");

        var envelope = await signBridge.CreateEnvelopeAsync(
            new EnvelopeRequest(
                Reference: leaseNumber,                    // <1>
                SignerName: lease.TenantName,
                SignerEmail: lease.TenantEmail,
                DocumentName: $"Lease {leaseNumber} – "
                    + lease.PropertyName),
            cancellationToken);

        await leases.SetStatusAsync(leaseNumber,
            LeaseStatus.AwaitingSignature, envelope.EnvelopeId,
            cancellationToken);
        return envelope.EnvelopeId;
    }
    // </listing:activity>

    [Function(nameof(SendReminder))]
    public Task SendReminder(
        [ActivityTrigger] string envelopeId,
        CancellationToken cancellationToken) =>
        signBridge.SendReminderAsync(envelopeId, cancellationToken);

    [Function(nameof(SetStatus))]
    public Task SetStatus(
        [ActivityTrigger] StatusChange change,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        return leases.SetStatusAsync(change.LeaseNumber,
            change.Status, null, cancellationToken);
    }
}

/// <summary>Starts one workflow per lease from the webhook's queue.</summary>
public sealed partial class LeaseSigningStarter(
    ILogger<LeaseSigningStarter> logger)
{
    // <listing:starter>
    [Function("StartLeaseSigning")]
    public async Task Run(
        [ServiceBusTrigger("lease-signing", Connection = "ServiceBus")]
        string leaseNumber,
        [DurableClient] DurableTaskClient client,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        var id = LeaseSigningOrchestration.InstanceIdFor(leaseNumber);
        var existing = await client.GetInstanceAsync(
            id, cancellationToken);                        // <1>

        if (existing is { IsRunning: true })
        {
            LogAlreadyRunning(logger, id);
            return;                                        // <2>
        }

        await client.ScheduleNewOrchestrationInstanceAsync(
            nameof(LeaseSigningOrchestration), leaseNumber,
            new StartOrchestrationOptions { InstanceId = id }, // <3>
            cancellationToken);
    }
    // </listing:starter>

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Signing workflow {InstanceId} is already running")]
    private static partial void LogAlreadyRunning(
        ILogger logger, string instanceId);
}
