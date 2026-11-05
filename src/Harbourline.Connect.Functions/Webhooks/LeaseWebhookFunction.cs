using System.Text.Json;
using Harbourline.Connect.Core.Dataverse;
using Harbourline.Connect.Core.Events;
using Harbourline.Connect.Functions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Harbourline.Connect.Functions.Webhooks;

/// <summary>The output of the webhook: a response and maybe a message.</summary>
public sealed class LeaseWebhookOutput
{
    [ServiceBusOutput("lease-signing", Connection = "ServiceBus")]
    public string? SigningRequest { get; set; }

    [HttpResult]
    public required IActionResult Response { get; set; }
}

/// <summary>
/// Receives the Dataverse webhook registered on Update of hl_lease
/// (filtering attribute hl_leasestatus), Chapter 9. It does no slow work:
/// it checks the event, queues a signing request and answers at once.
/// </summary>
public sealed partial class LeaseWebhookFunction(
    IConfiguration configuration,
    ILogger<LeaseWebhookFunction> logger)
{
    // <listing:webhook-function>
    [Function("LeaseStatusWebhook")]
    public async Task<LeaseWebhookOutput> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post",
            Route = "webhooks/lease-status")] HttpRequest request) // <1>
    {
        ArgumentNullException.ThrowIfNull(request);
        using var reader = new StreamReader(request.Body);
        var body = await reader.ReadToEndAsync();

        DataverseEvent evt;
        try
        {
            evt = DataverseEvent.Parse(body);
        }
        catch (Exception ex) when (
            ex is JsonException or KeyNotFoundException
                or InvalidOperationException)
        {
            return Reply(ApiResults.Problem(request, 400,
                "Not a Dataverse event", null));           // <2>
        }

        var expectedOrg = configuration["Dataverse:OrganizationId"];
        if (!Guid.TryParse(expectedOrg, out var org)
            || evt.OrganizationId != org)                  // <3>
        {
            LogWrongOrg(logger, evt.OrganizationId);
            return Reply(new StatusCodeResult(403));
        }

        if (evt.GetChoice(LeaseTable.Status)
            != (int)LeaseStatus.AwaitingSignature)         // <4>
        {
            return Reply(new OkResult());
        }

        var leaseNumber = evt.GetString(LeaseTable.Number);
        if (leaseNumber is null)                           // <5>
        {
            LogNoNumber(logger, evt.PrimaryEntityId);
            return Reply(new OkResult());
        }

        LogQueued(logger, leaseNumber, evt.CorrelationId);
        return new LeaseWebhookOutput
        {
            SigningRequest = leaseNumber,                  // <6>
            Response = new OkResult(),
        };
    }
    // </listing:webhook-function>

    private static LeaseWebhookOutput Reply(IActionResult result) =>
        new() { Response = result };

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Webhook from unexpected organization {OrgId}")]
    private static partial void LogWrongOrg(ILogger logger, Guid orgId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Lease {LeaseId} event has no lease number; add "
            + "hl_leasenumber to the step's post-image")]
    private static partial void LogNoNumber(ILogger logger, Guid leaseId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Queued signing for lease {LeaseNumber} "
            + "(correlation {CorrelationId})")]
    private static partial void LogQueued(
        ILogger logger, string leaseNumber, Guid correlationId);
}
