using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Harbourline.Connect.Core.Integrations;

// LedgerOne is the fictional finance system used throughout the book.
// Its API is described in samples/Harbourline.Mocks, which runs it
// locally with configurable failures.

public sealed class LedgerOneOptions
{
    public const string SectionName = "LedgerOne";

    [Required, Url]
    public string BaseUrl { get; set; } = "";

    /// <summary>
    /// In Azure this setting is a Key Vault reference, so the key itself
    /// never appears in app settings (Chapter 5).
    /// </summary>
    [Required]
    public string ApiKey { get; set; } = "";
}

// <listing:ledger-models>
public sealed record LedgerCustomer(
    [property: JsonPropertyName("customerId")] string CustomerId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("phone")] string? Phone,
    [property: JsonPropertyName("city")] string? City,
    [property: JsonPropertyName("modifiedUtc")]
        DateTimeOffset ModifiedUtc);

public sealed record LedgerInvoice(
    [property: JsonPropertyName("invoiceNumber")] string Number,
    [property: JsonPropertyName("leaseNumber")] string LeaseNumber,
    [property: JsonPropertyName("amount")] decimal Amount,
    [property: JsonPropertyName("dueDate")] DateOnly DueDate,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("modifiedUtc")]
        DateTimeOffset ModifiedUtc);
// </listing:ledger-models>

public sealed record LedgerInvoicePage(
    [property: JsonPropertyName("items")]
        IReadOnlyList<LedgerInvoice> Items,
    [property: JsonPropertyName("nextPage")] int? NextPage);

public sealed record LedgerCustomerPage(
    [property: JsonPropertyName("items")]
        IReadOnlyList<LedgerCustomer> Items,
    [property: JsonPropertyName("nextPage")] int? NextPage);

public sealed record LedgerOrder(
    [property: JsonPropertyName("orderNumber")] string OrderNumber,
    [property: JsonPropertyName("propertyCode")] string PropertyCode,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("amount")] decimal Amount);

public sealed record LedgerOrderReceipt(
    [property: JsonPropertyName("ledgerOrderId")] string LedgerOrderId);
