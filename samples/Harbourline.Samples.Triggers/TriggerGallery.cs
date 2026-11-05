using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Harbourline.Samples.Triggers;

/// <summary>
/// One function per trigger type used in this book. Each method shows
/// only the trigger signature; later chapters fill in real work.
/// </summary>
public sealed class TriggerGallery(ILogger<TriggerGallery> logger)
{
    // <listing:timer-trigger>
    [Function("NightlyInvoiceSync")]
    public void NightlyInvoiceSync(
        [TimerTrigger("0 0 2 * * *")] TimerInfo timer)       // <1>
    {
        logger.LogInformation(
            "Nightly sync started. Next run {Next}",
            timer.ScheduleStatus?.Next);
    }
    // </listing:timer-trigger>

    // <listing:servicebus-trigger>
    [Function("OnOrderSubmitted")]
    public void OnOrderSubmitted(
        [ServiceBusTrigger("orders",
            Connection = "ServiceBus")]                      // <1>
        ServiceBusReceivedMessage message)                   // <2>
    {
        logger.LogInformation(
            "Order message {MessageId}, delivery {Count}",
            message.MessageId, message.DeliveryCount);       // <3>
    }
    // </listing:servicebus-trigger>

    // <listing:blob-trigger>
    [Function("OnPropertyFileArrived")]
    public async Task OnPropertyFileArrivedAsync(
        [BlobTrigger("property-feed/{name}",
            Source = BlobTriggerSource.EventGrid,            // <1>
            Connection = "FeedStorage")] Stream file,
        string name,                                         // <2>
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(file);
        var header = await reader
            .ReadLineAsync(cancellationToken);               // <3>
        logger.LogInformation(
            "File {Name} header: {Header}", name, header);
    }
    // </listing:blob-trigger>

    // <listing:multi-output>
    [Function("RequestLeaseSignature")]
    public SignatureRequestOutput RequestLeaseSignature(
        [HttpTrigger(AuthorizationLevel.Function, "post",
            Route = "leases/{leaseId:guid}/signature")]
        HttpRequest request,
        Guid leaseId)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new SignatureRequestOutput
        {
            QueueMessage = leaseId.ToString(),               // <1>
            HttpResponse = new AcceptedResult()              // <2>
        };
    }
    // </listing:multi-output>
}

// <listing:multi-output-type>
public sealed class SignatureRequestOutput
{
    [QueueOutput("signature-requests",
        Connection = "AzureWebJobsStorage")]                 // <1>
    public string? QueueMessage { get; set; }

    [HttpResult]                                             // <2>
    public required IActionResult HttpResponse { get; set; }
}
// </listing:multi-output-type>
