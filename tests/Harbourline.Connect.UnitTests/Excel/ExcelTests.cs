using ClosedXML.Excel;
using Harbourline.Connect.Core.Excel;

namespace Harbourline.Connect.UnitTests.Excel;

public sealed class ExcelTests
{
    [Fact]
    public void FinanceWorkbook_HasHeadersFormatsAndRows()
    {
        using var stream = new MemoryStream();
        FinanceWorkbook.Write(
        [
            new InvoiceLine("INV-1", "LSE-1", "1 Quay Street", 1250.5m,
                new DateOnly(2026, 11, 1), "Open"),
        ], stream, "Test export");

        stream.Position = 0;
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet("Invoices");
        Assert.Equal("Invoice", sheet.Cell(3, 1).GetString());
        Assert.Equal(1250.5, sheet.Cell(4, 4).GetDouble());
        Assert.Equal("#,##0.00", sheet.Cell(4, 4).Style.NumberFormat.Format);
    }

    // <listing:test-excel-roundtrip>
    [Fact]
    public void PriceTemplate_RoundTrips_AndBadCellsAreReported()
    {
        using var stream = new MemoryStream();
        PriceImport.WriteTemplate(
            [("HBR-0001", 950m), ("HBR-0002", 980m), ("HBR-0003", 1000m)],
            stream);

        // Simulate finance filling in the yellow column.
        stream.Position = 0;
        using (var workbook = new XLWorkbook(stream))
        {
            var sheet = workbook.Worksheet(PriceImport.SheetName);
            sheet.Cell(2, 3).Value = 990;                  // valid
            sheet.Cell(3, 3).Value = "ten";                // not a number
            // row 4 left empty: unchanged
            stream.SetLength(0);
            workbook.SaveAs(stream);
        }

        stream.Position = 0;
        var result = PriceImport.Read(stream);

        var change = Assert.Single(result.Changes);
        Assert.Equal(("HBR-0001", 990m), (change.Code, change.NewRent));
        var error = Assert.Single(result.Errors);
        Assert.Equal((3, "C"), (error.Row, error.Column));
    }
    // </listing:test-excel-roundtrip>

    [Fact]
    public void StreamingWorkbook_IsReadableByExcelLibraries()
    {
        using var stream = new MemoryStream();
        StreamingWorkbook.Write(stream, "Readings", ["Meter", "Value"],
            Enumerable.Range(1, 1000)
                .Select(i => new object?[] { $"M{i}", (decimal)i }));

        stream.Position = 0;
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet("Readings");
        Assert.Equal(1001, sheet.LastRowUsed()!.RowNumber());
        Assert.Equal(1000, sheet.Cell(1001, 2).GetDouble());
    }

    [Fact]
    public void MissingSheet_IsReported()
    {
        using var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            workbook.Worksheets.Add("Other");
            workbook.SaveAs(stream);
        }

        stream.Position = 0;
        Assert.Single(PriceImport.Read(stream).Errors);
    }
}
