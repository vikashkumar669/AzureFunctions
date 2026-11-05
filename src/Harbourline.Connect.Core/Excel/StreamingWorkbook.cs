using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Harbourline.Connect.Core.Excel;

/// <summary>
/// Writes a plain worksheet row by row with the Open XML SDK's
/// OpenXmlWriter, so memory stays flat however many rows there are.
/// No styles: this is for data extracts, not reports.
/// </summary>
public static class StreamingWorkbook
{
    // <listing:excel-streaming>
    public static void Write(
        Stream output,
        string sheetName,
        IReadOnlyList<string> headers,
        IEnumerable<object?[]> rows)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);
        using var document = SpreadsheetDocument.Create(
            output, SpreadsheetDocumentType.Workbook);
        var workbookPart = document.AddWorkbookPart();
        var sheetPart = workbookPart.AddNewPart<WorksheetPart>();

        using (var writer = OpenXmlWriter.Create(sheetPart)) // <1>
        {
            writer.WriteStartElement(new Worksheet());
            writer.WriteStartElement(new SheetData());
            WriteRow(writer, headers);
            foreach (var row in rows)
            {
                WriteRow(writer, row);                     // <2>
            }

            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        workbookPart.Workbook = new Workbook(new Sheets(new Sheet
        {
            Name = sheetName,
            SheetId = 1U,
            Id = workbookPart.GetIdOfPart(sheetPart),
        }));
    }

    private static void WriteRow(
        OpenXmlWriter writer, IEnumerable<object?> values)
    {
        writer.WriteStartElement(new Row());
        foreach (var value in values)
        {
            writer.WriteElement(value switch               // <3>
            {
                null => new Cell(),
                decimal d => new Cell
                {
                    DataType = CellValues.Number,
                    CellValue = new CellValue(d),
                },
                int i => new Cell
                {
                    DataType = CellValues.Number,
                    CellValue = new CellValue(i),
                },
                _ => new Cell
                {
                    DataType = CellValues.InlineString,
                    InlineString = new InlineString(
                        new Text(Convert.ToString(value,
                            System.Globalization
                                .CultureInfo.InvariantCulture)
                            ?? "")),
                },
            });
        }

        writer.WriteEndElement();
    }
    // </listing:excel-streaming>
}
