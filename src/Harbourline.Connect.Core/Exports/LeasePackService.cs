using ClosedXML.Excel;
using Harbourline.Connect.Core.Common;
using Harbourline.Connect.Core.Dataverse;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Harbourline.Connect.Core.Exports;

/// <summary>A generated workbook, ready to return or upload.</summary>
public sealed record LeasePack(string FileName, byte[] Content);

/// <summary>
/// Builds the lease pack: lease summary plus its invoices, read AS the
/// calling user, so they only get a lease they can open in Dynamics 365
/// (Chapter 22).
/// </summary>
public sealed class LeasePackService(IDataverseUserScope users)
{
    // <listing:lease-pack>
    public Task<Outcome<LeasePack>> BuildAsync(
        Guid callerObjectId,
        string leaseNumber,
        CancellationToken ct) =>
        users.RunAsUserAsync(callerObjectId, async dataverse =>
        {
            var lease = await GetLeaseAsync(dataverse, leaseNumber, ct)
                .ConfigureAwait(false);
            if (lease is null)                             // <1>
            {
                return Outcome<LeasePack>.NotFound(
                    $"Lease {leaseNumber} was not found.");
            }

            var invoices = await GetInvoicesAsync(
                dataverse, lease.Id, ct).ConfigureAwait(false);

            using var stream = new MemoryStream();
            WriteWorkbook(lease, invoices, stream);        // <2>
            return Outcome<LeasePack>.Ok(new LeasePack(
                $"lease-pack-{leaseNumber}.xlsx", stream.ToArray()));
        });
    // </listing:lease-pack>

    private static async Task<Entity?> GetLeaseAsync(
        IOrganizationServiceAsync2 dataverse, string number,
        CancellationToken ct)
    {
        var query = new QueryExpression(LeaseTable.LogicalName)
        {
            ColumnSet = new ColumnSet(LeaseTable.Number,
                LeaseTable.Property, LeaseTable.Tenant,
                LeaseTable.StartDate, LeaseTable.EndDate,
                LeaseTable.MonthlyRent, LeaseTable.Status),
            TopCount = 1,
        };
        query.Criteria.AddCondition(LeaseTable.Number,
            ConditionOperator.Equal, number);
        var rows = await dataverse.RetrieveMultipleAsync(query, ct)
            .ConfigureAwait(false);
        return rows.Entities.FirstOrDefault();
    }

    private static async Task<IReadOnlyList<Entity>> GetInvoicesAsync(
        IOrganizationServiceAsync2 dataverse, Guid leaseId,
        CancellationToken ct)
    {
        var query = new QueryExpression(InvoiceTable.LogicalName)
        {
            ColumnSet = new ColumnSet(InvoiceTable.Number,
                InvoiceTable.Amount, InvoiceTable.DueDate,
                InvoiceTable.Status),
        };
        query.Criteria.AddCondition(InvoiceTable.Lease,
            ConditionOperator.Equal, leaseId);
        query.AddOrder(InvoiceTable.DueDate, OrderType.Descending);
        var rows = await dataverse.RetrieveMultipleAsync(query, ct)
            .ConfigureAwait(false);
        return rows.Entities;
    }

    private static void WriteWorkbook(
        Entity lease, IReadOnlyList<Entity> invoices, Stream output)
    {
        using var workbook = new XLWorkbook();
        var summary = workbook.Worksheets.Add("Lease");
        string[] labels =
            ["Lease", "Property", "Tenant", "Start", "End", "Rent"];
        object?[] values =
        [
            lease.GetAttributeValue<string>(LeaseTable.Number),
            lease.GetAttributeValue<EntityReference>(
                LeaseTable.Property)?.Name,
            lease.GetAttributeValue<EntityReference>(
                LeaseTable.Tenant)?.Name,
            lease.GetAttributeValue<DateTime?>(LeaseTable.StartDate),
            lease.GetAttributeValue<DateTime?>(LeaseTable.EndDate),
            lease.GetAttributeValue<Money>(LeaseTable.MonthlyRent)
                ?.Value,
        ];

        for (var i = 0; i < labels.Length; i++)
        {
            summary.Cell(i + 1, 1).Value = labels[i];
            summary.Cell(i + 1, 1).Style.Font.Bold = true;
            summary.Cell(i + 1, 2).Value = values[i] switch
            {
                DateTime d => d,
                decimal m => (double)m,
                null => Blank.Value,
                var v => v.ToString() ?? "",
            };
        }

        summary.Column(1).Width = 12;
        summary.Column(2).Width = 36;

        var sheet = workbook.Worksheets.Add("Invoices");
        sheet.Cell(1, 1).Value = "Invoice";
        sheet.Cell(1, 2).Value = "Amount";
        sheet.Cell(1, 3).Value = "Due date";
        sheet.Cell(1, 4).Value = "Status";
        var r = 2;
        foreach (var inv in invoices)
        {
            sheet.Cell(r, 1).Value =
                inv.GetAttributeValue<string>(InvoiceTable.Number);
            sheet.Cell(r, 2).Value = (double)(inv
                .GetAttributeValue<Money>(InvoiceTable.Amount)
                ?.Value ?? 0m);
            sheet.Cell(r, 3).Value = inv
                .GetAttributeValue<DateTime>(InvoiceTable.DueDate);
            sheet.Cell(r, 4).Value = inv.FormattedValues
                .TryGetValue(InvoiceTable.Status, out var label)
                    ? label : "";
            r++;
        }

        sheet.Column(2).Style.NumberFormat.Format = "#,##0.00";
        sheet.Column(3).Style.NumberFormat.Format = "yyyy-mm-dd";
        workbook.SaveAs(output);
    }
}
