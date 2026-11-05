using System.Buffers.Text;
using System.Text.Json;
using Harbourline.Connect.Contracts.Properties;
using Harbourline.Connect.Core.Dataverse;
using Harbourline.Connect.Core.Properties;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk.Query;

namespace Harbourline.Connect.Core.Search;

/// <summary>The last row of a page: where the next page starts.</summary>
public sealed record SearchCursor(string Name, Guid Id);

/// <summary>
/// The public property search of Chapter 23. Uses keyset paging (seek
/// past the last row by name and id) instead of page numbers, so deep
/// pages cost the same as the first, and caches popular first pages.
/// </summary>
public sealed class PropertySearch(
    IOrganizationServiceAsync2 dataverse, IMemoryCache cache)
{
    public static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(60);

    private static readonly string[] Columns =
    [
        PropertyTable.Code, PropertyTable.Name, PropertyTable.City,
        PropertyTable.Postcode, PropertyTable.Bedrooms,
        PropertyTable.MonthlyRent, PropertyTable.Status,
        PropertyTable.AvailableFrom, PropertyTable.Id,
    ];

    public async Task<PropertyPage> SearchAsync(
        string? city, int? minBedrooms, string? after, int size,
        CancellationToken ct)
    {
        size = Math.Clamp(size, 1, 100);
        var cursor = DecodeCursor(after);
        if (cursor is not null)
        {
            return await QueryAsync(city, minBedrooms, cursor, size, ct)
                .ConfigureAwait(false);
        }

        // <listing:search-cache>
        var key = $"search:{city?.ToUpperInvariant()}:"
            + $"{minBedrooms}:{size}";                     // <1>
        return (await cache.GetOrCreateAsync(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheFor; // <2>
            return QueryAsync(city, minBedrooms, null, size, ct);
        }).ConfigureAwait(false))!;
        // </listing:search-cache>
    }

    // <listing:keyset>
    private async Task<PropertyPage> QueryAsync(
        string? city, int? minBedrooms, SearchCursor? after,
        int size, CancellationToken ct)
    {
        var query = new QueryExpression(PropertyTable.LogicalName)
        {
            ColumnSet = new ColumnSet(Columns),
            TopCount = size + 1,                           // <1>
        };
        query.AddOrder(PropertyTable.Name, OrderType.Ascending);
        query.AddOrder(PropertyTable.Id, OrderType.Ascending);
        query.Criteria.AddCondition(PropertyTable.Status,
            ConditionOperator.Equal, (int)PropertyStatus.Available);

        if (!string.IsNullOrWhiteSpace(city))
        {
            query.Criteria.AddCondition(PropertyTable.City,
                ConditionOperator.Equal, city.Trim());
        }

        if (minBedrooms is { } min)
        {
            query.Criteria.AddCondition(PropertyTable.Bedrooms,
                ConditionOperator.GreaterEqual, min);
        }

        if (after is not null)                             // <2>
        {
            var seek = new FilterExpression(LogicalOperator.Or);
            seek.AddCondition(PropertyTable.Name,
                ConditionOperator.GreaterThan, after.Name);
            var tie = seek.AddFilter(LogicalOperator.And);
            tie.AddCondition(PropertyTable.Name,
                ConditionOperator.Equal, after.Name);
            tie.AddCondition(PropertyTable.Id,
                ConditionOperator.GreaterThan, after.Id);
            query.Criteria.AddFilter(seek);
        }

        var rows = (await dataverse.RetrieveMultipleAsync(query, ct)
            .ConfigureAwait(false)).Entities;
        var page = rows.Take(size).ToList();
        string? next = null;
        if (rows.Count > size)                             // <3>
        {
            var last = page[^1];
            next = EncodeCursor(new SearchCursor(
                last.GetAttributeValue<string>(PropertyTable.Name),
                last.Id));
        }

        return new PropertyPage(
            page.Select(PropertyMapper.ToDto).ToList(), next);
    }
    // </listing:keyset>

    public static string EncodeCursor(SearchCursor cursor) =>
        Base64Url.EncodeToString(
            JsonSerializer.SerializeToUtf8Bytes(cursor));

    public static SearchCursor? DecodeCursor(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SearchCursor>(
                Base64Url.DecodeFromChars(value));
        }
        catch (Exception ex) when (
            ex is FormatException or JsonException)
        {
            throw new FormatException("The cursor is not valid.", ex);
        }
    }
}
