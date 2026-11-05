using ClosedXML.Excel;
using Harbourline.Connect.Core.Properties;

namespace Harbourline.Connect.Core.Excel;

public sealed record PriceChange(int Row, string Code, decimal NewRent);

public sealed record SheetError(int Row, string Column, string Message);

public sealed record PriceSheet(
    IReadOnlyList<PriceChange> Changes,
    IReadOnlyList<SheetError> Errors);

/// <summary>
/// Reads the bulk price-change template (Chapter 17). Column A is the
/// property code, column C the new monthly rent. Every bad cell is
/// reported with its row number so finance can fix and re-upload.
/// </summary>
public static class PriceImport
{
    public const string SheetName = "Prices";

    // <listing:excel-template>
    public static void WriteTemplate(
        IEnumerable<(string Code, decimal Rent)> current, Stream output)
    {
        ArgumentNullException.ThrowIfNull(current);
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(SheetName);
        sheet.Cell(1, 1).Value = "Property code";
        sheet.Cell(1, 2).Value = "Current rent";
        sheet.Cell(1, 3).Value = "New rent";

        var r = 2;
        foreach (var (code, rent) in current)
        {
            sheet.Cell(r, 1).Value = code;
            sheet.Cell(r, 2).Value = (double)rent;
            r++;
        }

        var last = Math.Max(r - 1, 2);
        var newRent = sheet.Range(2, 3, last, 3);
        newRent.CreateDataValidation().Decimal                // <1>
            .Between(1, (double)PropertyValidator.MaxMonthlyRent);
        newRent.Style.Fill.BackgroundColor =
            XLColor.LightYellow;                           // <2>

        sheet.Protect().AllowElement(
            XLSheetProtectionElements.SelectEverything);   // <3>
        newRent.Style.Protection.Locked = false;
        sheet.Column(1).Width = 16;
        sheet.Column(2).Width = 14;
        sheet.Column(3).Width = 14;
        workbook.SaveAs(output);
    }
    // </listing:excel-template>

    // <listing:excel-import>
    public static PriceSheet Read(Stream input)
    {
        using var workbook = new XLWorkbook(input);
        if (!workbook.TryGetWorksheet(SheetName, out var sheet))
        {
            return new PriceSheet([], [new SheetError(0, "",
                $"The workbook has no '{SheetName}' sheet.")]);
        }

        var changes = new List<PriceChange>();
        var errors = new List<SheetError>();

        foreach (var row in sheet.RowsUsed().Skip(1))      // <1>
        {
            var n = row.RowNumber();
            var code = row.Cell(1).GetString().Trim();
            var rentCell = row.Cell(3);

            if (rentCell.IsEmpty())
            {
                continue;                                  // <2>
            }

            if (!PropertyValidator.IsValidCode(code))
            {
                errors.Add(new SheetError(n, "A",
                    $"'{code}' is not a property code."));
            }
            else if (!rentCell.TryGetValue<decimal>(out var rent)
                || rent <= 0
                || rent > PropertyValidator.MaxMonthlyRent)
            {
                errors.Add(new SheetError(n, "C",               // <3>
                    "New rent must be a number from 1 to "
                    + PropertyValidator.MaxMonthlyRent + "."));
            }
            else
            {
                changes.Add(new PriceChange(n, code, rent));
            }
        }

        return new PriceSheet(changes, errors);
    }
    // </listing:excel-import>
}
