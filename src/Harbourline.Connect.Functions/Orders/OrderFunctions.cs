using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Harbourline.Connect.Contracts.Orders;
using Harbourline.Connect.Core.Orders;
using Harbourline.Connect.Core.Telemetry;
using Harbourline.Connect.Functions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Harbourline.Connect.Functions.Orders;

/// <summary>
/// The order-intake pipeline of Chapter 24: accept → queue → process,
/// with permanent failures dead-lettered at once and transient ones
/// retried by Service Bus.
/// </summary>
public sealed partial class OrderFunctions(
    ServiceBusSender intake,
    OrderProcessor processor,
    OrderStatusReader status,
    ILogger<OrderFunctions> logger)
{
    public const string Queue = "order-intake";
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web);

    // <listing:order-accept>
    [Function("SubmitOrder")]
    public async Task<IActionResult> Submit(
        [HttpTrigger(AuthorizationLevel.Function, "post",
            Route = "v1/orders")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        OrderSubmission? order;
        try
        {
            order = await JsonSerializer.DeserializeAsync<
                OrderSubmission>(request.Body, Json, cancellationToken);
        }
        catch (JsonException)
        {
            order = null;
        }

        if (order is null)
        {
            return ApiResults.Problem(request, 400, "Invalid JSON", null);
        }

        var errors = OrderValidator.Validate(order);       // <1>
        if (!errors.IsValid)
        {
            return new BadRequestObjectResult(
                new ValidationProblemDetails(
                    errors.ToDictionary().ToDictionary()));
        }

        await intake.SendMessageAsync(new ServiceBusMessage(
            BinaryData.FromObjectAsJson(order, Json))
        {
            MessageId = order.OrderNumber,                 // <2>
            ContentType = "application/json",
            Subject = order.Category,
        }, cancellationToken);

        var statusUrl = $"/api/v1/orders/{order.OrderNumber}";
        return new AcceptedResult(statusUrl,               // <3>
            new OrderAccepted(order.OrderNumber!, statusUrl));
    }
    // </listing:order-accept>

    // <listing:order-process-fn>
    [Function("ProcessOrder")]
    public async Task Process(
        [ServiceBusTrigger(Queue, Connection = "ServiceBus",
            AutoCompleteMessages = false)]
        ServiceBusReceivedMessage message,
        ServiceBusMessageActions actions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(actions);
        using var activity = HarbourlineTelemetry.Source
            .StartActivity("ProcessOrder");                // <1>
        activity?.SetTag("order.number", message.MessageId);
        activity?.SetTag("delivery.count", message.DeliveryCount);

        try
        {
            var order = message.Body.ToObjectFromJson<
                OrderSubmission>(Json)
                ?? throw new PermanentFailureException("Empty body.");
            var ledgerId = await processor.ProcessAsync(
                order, cancellationToken);
            await actions.CompleteMessageAsync(
                message, cancellationToken);
            LogProcessed(logger, message.MessageId, ledgerId);
        }
        catch (Exception ex) when (
            ex is JsonException
            || !FailureClassifier.IsTransient(ex))         // <2>
        {
            LogDeadLetter(logger, ex, message.MessageId);
            HarbourlineTelemetry.MessagesDeadLettered.Add(1,
                new KeyValuePair<string, object?>("queue", Queue));
            await actions.DeadLetterMessageAsync(message,
                deadLetterReason: ex is PermanentFailureException
                    ? "Permanent" : ex.GetType().Name,
                deadLetterErrorDescription: ex.Message.Length > 900
                    ? ex.Message[..900] : ex.Message,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (
            FailureClassifier.IsTransient(ex))             // <3>
        {
            LogRetry(logger, ex, message.MessageId,
                message.DeliveryCount);
            await actions.AbandonMessageAsync(
                message, cancellationToken: cancellationToken);
            throw;
        }
    }
    // </listing:order-process-fn>

    [Function("GetOrderStatus")]
    public async Task<IActionResult> Status(
        [HttpTrigger(AuthorizationLevel.Function, "get",
            Route = "v1/orders/{number}")] HttpRequest request,
        string number,
        CancellationToken cancellationToken)
    {
        var view = await status.GetAsync(number, cancellationToken);
        return view is null
            ? ApiResults.Problem(request, 404, "Not found",
                $"Order {number} has not been processed yet.")
            : new OkObjectResult(view);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Order {OrderNumber} processed as {LedgerId}")]
    private static partial void LogProcessed(
        ILogger logger, string orderNumber, string ledgerId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Order {OrderNumber} failed on delivery {Count}; "
            + "Service Bus will retry")]
    private static partial void LogRetry(ILogger logger,
        Exception exception, string orderNumber, int count);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Order {OrderNumber} dead-lettered")]
    private static partial void LogDeadLetter(
        ILogger logger, Exception exception, string orderNumber);
}
