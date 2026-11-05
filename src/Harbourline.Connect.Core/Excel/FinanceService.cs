using Harbourline.Connect.Core.Bulk;
using Harbourline.Connect.Core.Dataverse;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Harbourline.Connect.Core.Excel;

public sealed record PriceImportResult(
    int Applied, IReadOnlyList<SheetError> Errors);

/// <summary>Finance's Excel export and bulk price import (Chapter 17).</summary>
public sealed class FinanceService(
    IOrganizationServiceAsync2 dataverse, BulkWriter writer)
{
    // <listing:finance-query>
    public async Task<IReadOnlyList<InvoiceLine>> GetInvoicesAsync(
        DateOnly month, CancellationToken ct)
    {
        var from = month.ToDateTime(TimeOnly.MinValue);
        var query = new QueryExpression(InvoiceTable.LogicalName)
        {
            ColumnSet = new ColumnSet(InvoiceTable.Number,
                InvoiceTable.Amount, InvoiceTable.DueDate,
                InvoiceTable.Status),
            PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 },
        };
        query.Criteria.AddCondition(InvoiceTable.DueDate,
            ConditionOperator.OnOrAfter, from);
        query.Criteria.AddCondition(InvoiceTable.DueDate,
            ConditionOperator.LessThan, from.AddMonths(1));
        query.AddOrder(InvoiceTable.Number, OrderType.Ascending);

        var lease = query.AddLink(LeaseTable.LogicalName,
            InvoiceTable.Lease, LeaseTable.Id);
        lease.EntityAlias = "l";
        lease.Columns = new ColumnSet(
            LeaseTable.Number, LeaseTable.Property);       // <1>

        var lines = new List<InvoiceLine>();
        while (true)
        {
            var page = await dataverse.RetrieveMultipleAsync(query, ct)
                .ConfigureAwait(false);
            lines.AddRange(page.Entities.Select(ToLine));
            if (!page.MoreRecords)
            {
                return lines;
            }

            query.PageInfo.PageNumber++;
            query.PageInfo.PagingCookie = page.PagingCookie;
        }
    }

    private static InvoiceLine ToLine(Entity e) => new(
        e.GetAttributeValue<string>(InvoiceTable.Number),
        e.GetAttributeValue<AliasedValue>("l." + LeaseTable.Number)
            ?.Value as string ?? "",
        (e.GetAttributeValue<AliasedValue>("l." + LeaseTable.Property)
            ?.Value as EntityReference)?.Name ?? "",       // <2>
        e.GetAttributeValue<Money>(InvoiceTable.Amount)?.Value ?? 0m,
        DateOnly.FromDateTime(
            e.GetAttributeValue<DateTime>(InvoiceTable.DueDate)),
        e.FormattedValues.TryGetValue(
            InvoiceTable.Status, out var s) ? s : "");     // <3>
    // </listing:finance-query>

    // <listing:price-apply>
    public async Task<PriceImportResult> ApplyAsync(
        PriceSheet sheet, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        var errors = sheet.Errors.ToList();
        var known = await ExistingCodesAsync(
            sheet.Changes.Select(c => c.Code).Distinct().ToList(), ct)
            .ConfigureAwait(false);

        var valid = new List<PriceChange>();
        foreach (var change in sheet.Changes)
        {
            if (known.Contains(change.Code))
            {
                valid.Add(change);
            }
            else
            {
                errors.Add(new SheetError(change.Row, "A",
                    $"Property {change.Code} does not exist."));
            }
        }

        var rows = valid.Select(c => new Entity(
            PropertyTable.LogicalName, PropertyTable.Code, c.Code)
        {
            [PropertyTable.MonthlyRent] = new Money(c.NewRent),
        }).ToList();                                       // <1>

        var result = await writer.UpsertAsync(
            PropertyTable.LogicalName, rows,
            r => (string)r.KeyAttributes[PropertyTable.Code],
            new BulkOptions(BatchSize: 100, Parallelism: 2), ct)
            .ConfigureAwait(false);

        errors.AddRange(result.Errors.Select(e =>
            new SheetError(valid[e.Index].Row, "C", e.Message)));
        return new PriceImportResult(result.Succeeded,
            errors.OrderBy(e => e.Row).ToList());
    }
    // </listing:price-apply>

    private async Task<HashSet<string>> ExistingCodesAsync(
        IReadOnlyList<string> codes, CancellationToken ct)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var chunk in codes.Chunk(500))            // In ≤ 500
        {
            var query = new QueryExpression(PropertyTable.LogicalName)
            {
                ColumnSet = new ColumnSet(PropertyTable.Code),
            };
            query.Criteria.AddCondition(PropertyTable.Code,
                ConditionOperator.In, chunk.Cast<object>().ToArray());
            var rows = await dataverse.RetrieveMultipleAsync(query, ct)
                .ConfigureAwait(false);
            found.UnionWith(rows.Entities.Select(r =>
                r.GetAttributeValue<string>(PropertyTable.Code)));
        }

        return found;
    }
}
