using System.Text.Json.Serialization;

namespace Harbourline.Connect.Contracts.Properties;

// Public contract of the Property API (Chapter 7). These types are what
// callers see; they never expose Dataverse types or logical names.

// <listing:property-dto>
/// <summary>A property as the tenant portal sees it.</summary>
public sealed record PropertyDto(
    string Code,
    string Name,
    string City,
    string Postcode,
    int Bedrooms,
    decimal MonthlyRent,
    string Status,
    DateOnly? AvailableFrom)
{
    /// <summary>Version for optimistic concurrency (ETag).</summary>
    [JsonIgnore]
    public string? Version { get; init; }
}

/// <summary>One page of results plus a token for the next page.</summary>
public sealed record PropertyPage(
    IReadOnlyList<PropertyDto> Items,
    string? ContinuationToken);
// </listing:property-dto>

// <listing:property-requests>
public sealed record CreatePropertyRequest(
    string? Code,
    string? Name,
    string? City,
    string? Postcode,
    int? Bedrooms,
    decimal? MonthlyRent,
    DateOnly? AvailableFrom);

/// <summary>PATCH body: only the fields present are changed.</summary>
public sealed record UpdatePropertyRequest(
    string? Name,
    int? Bedrooms,
    decimal? MonthlyRent,
    string? Status,
    DateOnly? AvailableFrom);
// </listing:property-requests>

/// <summary>Query-string filters for GET /v1/properties.</summary>
public sealed record PropertyQuery(
    string? City,
    string? Status,
    int? MinBedrooms,
    int PageSize = 50);
