using System.Text.Json;
using System.Text.Json.Serialization;
using Harbourline.Connect.Core.Integrations;
using Harbourline.Connect.Functions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Options;

namespace Harbourline.Connect.Functions.Leases;

public sealed record SignBridgeCallback(
    [property: JsonPropertyName("envelopeId")] string EnvelopeId,
    [property: JsonPropertyName("reference")] string Reference,
    [property: JsonPropertyName("status")] string Status);

/// <summary>
/// SignBridge calls this when a tenant signs or declines. The function
/// verifies the signature, then raises the event the orchestration is
/// waiting for (Chapter 12).
/// </summary>
public sealed class SignBridgeCallbackFunction(
    IOptions<SignBridgeOptions> options)
{
    // <listing:callback>
    [Function("SignBridgeCallback")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post",
            Route = "callbacks/signbridge")] HttpRequest request, // <1>
        [DurableClient] DurableTaskClient client,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(client);
        using var reader = new StreamReader(request.Body);
        var body = await reader.ReadToEndAsync(cancellationToken);

        if (!CallbackSignature.IsValid(body,
                request.Headers["X-SignBridge-Signature"],
                options.Value.CallbackSecret))             // <2>
        {
            return new UnauthorizedResult();
        }

        var callback = JsonSerializer
            .Deserialize<SignBridgeCallback>(body);
        if (callback is null)
        {
            return ApiResults.Problem(request, 400,
                "Invalid callback", null);
        }

        var id = LeaseSigningOrchestration
            .InstanceIdFor(callback.Reference);
        var instance = await client.GetInstanceAsync(
            id, cancellationToken);
        if (instance is not { IsRunning: true })           // <3>
        {
            return new OkResult();
        }

        await client.RaiseEventAsync(id,
            LeaseSigningOrchestration.SignedEvent,
            new SignatureResult(callback.EnvelopeId, callback.Status),
            cancellationToken);                            // <4>
        return new AcceptedResult();
    }
    // </listing:callback>
}
