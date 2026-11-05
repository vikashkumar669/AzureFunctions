using ClosedXML.Excel;

namespace Harbourline.Connect.Core.Excel;

/// <summary>One invoice line in the monthly finance export.</summary>
public sealed record InvoiceLine(
    string InvoiceNumber,
    string LeaseNumber,
    string PropertyName,
    decimal Amount,
    DateOnly DueDate,
    string Status);

/// <summary>
/// Builds the formatted finance workbook with ClosedXML (MIT licence).
/// Suits workbooks up to tens of thousands of rows; larger exports use
/// StreamingWorkbook (Chapter 17).
/// </summary>
public static class FinanceWorkbook
{
    public static readonly string[] Headers =
        ["Invoice", "Lease", "Property", "Amount", "Due date", "Status"];

    // <listing:excel-export>
    public static void Write(
        IEnumerable<InvoiceLine> lines, Stream output, string title)
    {
        ArgumentNullException.ThrowIfNull(lines);
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Invoices");

        sheet.Cell(1, 1).Value = title;                    // <1>
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 14;

        for (var c = 0; c < Headers.Length; c++)
        {
            sheet.Cell(3, c + 1).Value = Headers[c];
        }

        var r = 4;
        foreach (var line in lines)
        {
            sheet.Cell(r, 1).Value = line.InvoiceNumber;
            sheet.Cell(r, 2).Value = line.LeaseNumber;
            sheet.Cell(r, 3).Value = line.PropertyName;
            sheet.Cell(r, 4).Value = (double)line.Amount;  // <2>
            sheet.Cell(r, 5).Value =
                line.DueDate.ToDateTime(TimeOnly.MinValue);
            sheet.Cell(r, 6).Value = line.Status;
            r++;
        }

        var last = Math.Max(r - 1, 4);
        sheet.Range(4, 4, last, 4).Style.NumberFormat.Format =
            "#,##0.00";                                    // <3>
        sheet.Range(4, 5, last, 5).Style.NumberFormat.Format =
            "yyyy-mm-dd";
        sheet.Range(3, 1, last, Headers.Length)
            .CreateTable("InvoiceTable");                  // <4>
        sheet.SheetView.FreezeRows(3);

        double[] widths = [14, 14, 32, 12, 12, 12];        // <5>
        for (var c = 0; c < widths.Length; c++)
        {
            sheet.Column(c + 1).Width = widths[c];
        }

        workbook.SaveAs(output);
    }
    // </listing:excel-export>
}
