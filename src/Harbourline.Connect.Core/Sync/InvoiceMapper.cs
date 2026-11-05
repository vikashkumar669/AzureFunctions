using Harbourline.Connect.Core.Dataverse;
using Harbourline.Connect.Core.Integrations;
using Microsoft.Xrm.Sdk;

namespace Harbourline.Connect.Core.Sync;

public static class InvoiceMapper
{
    // <listing:invoice-map>
    public static Entity ToEntity(LedgerInvoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        return new Entity(InvoiceTable.LogicalName,
            InvoiceTable.Number, invoice.Number)           // <1>
        {
            [InvoiceTable.Name] = invoice.Number,
            [InvoiceTable.Lease] = new EntityReference(
                LeaseTable.LogicalName,
                LeaseTable.Number, invoice.LeaseNumber),   // <2>
            [InvoiceTable.Amount] = new Money(invoice.Amount),
            [InvoiceTable.DueDate] =
                invoice.DueDate.ToDateTime(TimeOnly.MinValue),
            [InvoiceTable.Status] =
                new OptionSetValue((int)StatusOf(invoice.Status)),
        };
    }

    public static InvoiceStatus StatusOf(string ledgerStatus) =>
        ledgerStatus.ToUpperInvariant() switch            // <3>
        {
            "OPEN" or "ISSUED" => InvoiceStatus.Open,
            "PAID" => InvoiceStatus.Paid,
            "OVERDUE" => InvoiceStatus.Overdue,
            "VOID" or "CANCELLED" => InvoiceStatus.Cancelled,
            _ => throw new ArgumentOutOfRangeException(
                nameof(ledgerStatus), ledgerStatus,
                "Unknown LedgerOne invoice status."),
        };
    // </listing:invoice-map>
}
