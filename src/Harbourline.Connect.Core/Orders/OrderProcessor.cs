using System.Net;
using Harbourline.Connect.Contracts.Orders;
using Harbourline.Connect.Core.Common;
using Harbourline.Connect.Core.Dataverse;
using Harbourline.Connect.Core.Integrations;
using Harbourline.Connect.Core.Properties;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Polly.CircuitBreaker;

namespace Harbourline.Connect.Core.Orders;

/// <summary>
/// Thrown when retrying cannot help: the message itself is wrong. The
/// Service Bus handler dead-letters these at once (Chapter 24).
/// </summary>
public sealed class PermanentFailureException : Exception
{
    public PermanentFailureException() { }
    public PermanentFailureException(string message) : base(message) { }
    public PermanentFailureException(string message, Exception inner)
        : base(message, inner) { }
}

public static class OrderValidator
{
    public static readonly string[] Categories =
        ["Plumbing", "Electrical", "Heating", "Cleaning", "General"];

    public static ValidationErrors Validate(OrderSubmission order)
    {
        ArgumentNullException.ThrowIfNull(order);
        var errors = new ValidationErrors();
        errors.Require("orderNumber", order.OrderNumber, 20);
        errors.Require("partnerId", order.PartnerId, 40);

        if (!PropertyValidator.IsValidCode(order.PropertyCode))
        {
            errors.Add("propertyCode", "propertyCode must look like ABC-1234.");
        }

        if (!Categories.Contains(order.Category,
                StringComparer.OrdinalIgnoreCase))
        {
            errors.Add("category",
                "category must be one of: "
                + string.Join(", ", Categories) + ".");
        }

        if (order.Amount is null or <= 0 or > 50_000)
        {
            errors.Add("amount", "amount must be above 0 and at most 50000.");
        }

        return errors;
    }
}

public static class FailureClassifier
{
    // <listing:failure-classifier>
    public static bool IsTransient(Exception exception) =>
        exception switch
        {
            PermanentFailureException => false,            // <1>
            HttpRequestException { StatusCode: null } => true, // <2>
            HttpRequestException { StatusCode: { } status } =>
                (int)status >= 500
                || status is HttpStatusCode.TooManyRequests
                    or HttpStatusCode.RequestTimeout,      // <3>
            TimeoutException or BrokenCircuitException => true,
            _ when DataverseErrors.Classify(exception) is { } dv =>
                dv.Kind is DataverseErrorKind.Throttled
                    or DataverseErrorKind.Unknown,         // <4>
            _ => true,                                     // <5>
        };
    // </listing:failure-classifier>
}

/// <summary>Validates an order, records it, and submits it to LedgerOne.</summary>
public sealed class OrderProcessor(
    IOrganizationServiceAsync2 dataverse, ILedgerOneClient ledger)
{
    // <listing:order-process>
    public async Task<string> ProcessAsync(
        OrderSubmission order, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(order);
        var errors = OrderValidator.Validate(order);
        if (!errors.IsValid)
        {
            throw new PermanentFailureException(            // <1>
                string.Join("; ", errors.ToDictionary()
                    .SelectMany(e => e.Value)));
        }

        var row = new Entity(OrderTable.LogicalName,
            OrderTable.Number, order.OrderNumber)          // <2>
        {
            [OrderTable.Name] = order.OrderNumber,
            [OrderTable.Property] = new EntityReference(
                PropertyTable.LogicalName,
                PropertyTable.Code, order.PropertyCode),
            [OrderTable.Category] = order.Category,
            [OrderTable.Amount] = new Money(order.Amount!.Value),
            [OrderTable.Status] =
                new OptionSetValue((int)OrderStatus.Received),
        };

        try
        {
            await dataverse.ExecuteAsync(
                new UpsertRequest { Target = row }, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (DataverseErrors.Classify(ex)?.Kind
            is DataverseErrorKind.NotFound)                // <3>
        {
            throw new PermanentFailureException(
                $"Property {order.PropertyCode} does not exist.", ex);
        }

        var receipt = await ledger.SubmitOrderAsync(       // <4>
            new LedgerOrder(order.OrderNumber!, order.PropertyCode!,
                order.Category!, order.Amount!.Value), ct)
            .ConfigureAwait(false);

        row[OrderTable.Status] =
            new OptionSetValue((int)OrderStatus.Approved);
        await dataverse.UpdateAsync(row, ct).ConfigureAwait(false);
        return receipt.LedgerOrderId;
    }
    // </listing:order-process>
}
