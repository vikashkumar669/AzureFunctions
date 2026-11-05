namespace Harbourline.Connect.Contracts.Orders;

// <listing:order-contract>
/// <summary>A service order from a maintenance partner.</summary>
public sealed record OrderSubmission(
    string? OrderNumber,
    string? PropertyCode,
    string? Category,
    decimal? Amount,
    string? PartnerId);

/// <summary>Accepted, but not processed yet.</summary>
public sealed record OrderAccepted(string OrderNumber, string StatusUrl);
// </listing:order-contract>
