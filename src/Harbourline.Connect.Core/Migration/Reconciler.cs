using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Harbourline.Connect.Core.Dataverse;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Harbourline.Connect.Core.Migration;

public sealed record ReconciliationReport(
    int SourceCount,
    int TargetCount,
    IReadOnlyList<string> MissingInTarget,
    IReadOnlyList<string> Different,
    string SourceChecksum,
    string TargetChecksum)
{
    public bool Matches =>
        MissingInTarget.Count == 0 && Different.Count == 0;
}

/// <summary>
/// Compares what should be in Dataverse with what is there: counts, a
/// per-row fingerprint, and one checksum for the whole set.
/// </summary>
public sealed class Reconciler(IOrganizationServiceAsync2 dataverse)
{
    private static readonly string[] Columns =
    [
        PropertyTable.Code, PropertyTable.Name, PropertyTable.City,
        PropertyTable.Postcode, PropertyTable.Bedrooms,
        PropertyTable.MonthlyRent, PropertyTable.Status,
    ];

    // <listing:fingerprint>
    public static string Fingerprint(Entity row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return string.Join('|',                            // <1>
            row.GetAttributeValue<string>(PropertyTable.Code),
            row.GetAttributeValue<string>(PropertyTable.Name),
            row.GetAttributeValue<string>(PropertyTable.City),
            row.GetAttributeValue<string>(PropertyTable.Postcode),
            row.GetAttributeValue<int>(PropertyTable.Bedrooms)
                .ToString(CultureInfo.InvariantCulture),
            (row.GetAttributeValue<Money>(PropertyTable.MonthlyRent)
                ?.Value ?? 0m).ToString("0.00",
                    CultureInfo.InvariantCulture),         // <2>
            row.GetAttributeValue<OptionSetValue>(
                PropertyTable.Status)?.Value
                .ToString(CultureInfo.InvariantCulture));
    }

    public static string Checksum(IEnumerable<string> fingerprints)
    {
        var all = string.Join('\n',
            fingerprints.Order(StringComparer.Ordinal));   // <3>
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(all)));
    }
    // </listing:fingerprint>

    // <listing:reconcile>
    public async Task<ReconciliationReport> ReconcileAsync(
        IReadOnlyList<Entity> expected,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var source = expected.ToDictionary(
            r => r.GetAttributeValue<string>(PropertyTable.Code),
            Fingerprint, StringComparer.Ordinal);
        var target = await ReadTargetAsync(cancellationToken)
            .ConfigureAwait(false);

        var missing = source.Keys
            .Where(k => !target.ContainsKey(k)).Order().ToList();
        var different = source
            .Where(s => target.TryGetValue(s.Key, out var t)
                && t != s.Value)
            .Select(s => s.Key).Order().ToList();

        var inScope = target                              // <1>
            .Where(t => source.ContainsKey(t.Key))
            .Select(t => t.Value);

        return new ReconciliationReport(
            source.Count, target.Count, missing, different,
            Checksum(source.Values), Checksum(inScope));
    }
    // </listing:reconcile>

    private async Task<Dictionary<string, string>> ReadTargetAsync(
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string>(
            StringComparer.Ordinal);
        var query = new QueryExpression(PropertyTable.LogicalName)
        {
            ColumnSet = new ColumnSet(Columns),
            PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 },
        };
        query.AddOrder(PropertyTable.Id, OrderType.Ascending);

        while (true)
        {
            var page = await dataverse
                .RetrieveMultipleAsync(query, cancellationToken)
                .ConfigureAwait(false);
            foreach (var row in page.Entities)
            {
                result[row.GetAttributeValue<string>(
                    PropertyTable.Code)] = Fingerprint(row);
            }

            if (!page.MoreRecords)
            {
                return result;
            }

            query.PageInfo.PageNumber++;
            query.PageInfo.PagingCookie = page.PagingCookie;
        }
    }
}
