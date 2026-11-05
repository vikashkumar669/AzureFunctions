using System.Globalization;
using Harbourline.Connect.Core.Excel;
using Harbourline.Connect.Functions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace Harbourline.Connect.Functions.Excel;

/// <summary>Excel export and import endpoints for finance (Chapter 17).</summary>
public sealed class FinanceExcelFunctions(FinanceService finance)
{
    private const string Xlsx =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>Uploads larger than this are refused.</summary>
    public const long MaxUploadBytes = 5 * 1024 * 1024;

    // <listing:fn-export>
    [Function("ExportInvoices")]
    public async Task<IActionResult> Export(
        [HttpTrigger(AuthorizationLevel.Function, "get",
            Route = "v1/finance/invoices/{month}")] HttpRequest request,
        string month,
        CancellationToken cancellationToken)
    {
        if (!DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None,
                out var first))
        {
            return ApiResults.Problem(request, 400, "Invalid month",
                "Use the form yyyy-MM, for example 2026-09.");
        }

        var lines = await finance.GetInvoicesAsync(
            first, cancellationToken);
        var stream = new MemoryStream();
        FinanceWorkbook.Write(lines, stream,
            $"Harbourline invoices – {month}");
        stream.Position = 0;

        return new FileStreamResult(stream, Xlsx)          // <1>
        {
            FileDownloadName = $"invoices-{month}.xlsx",
        };
    }
    // </listing:fn-export>

    // <listing:fn-import>
    [Function("ImportPrices")]
    public async Task<IActionResult> Import(
        [HttpTrigger(AuthorizationLevel.Function, "post",
            Route = "v1/finance/prices")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ContentLength is null or > MaxUploadBytes)  // <1>
        {
            return ApiResults.Problem(request, 413,
                "Upload too large", $"The limit is {MaxUploadBytes} bytes.");
        }

        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

        PriceSheet sheet;
        try
        {
            sheet = PriceImport.Read(buffer);              // <2>
        }
        catch (Exception ex) when (ex is InvalidDataException
            or FileFormatException
            or DocumentFormat.OpenXml.Packaging
                .OpenXmlPackageException)
        {
            return ApiResults.Problem(request, 400,
                "Not an Excel workbook", null);
        }

        var result = await finance.ApplyAsync(
            sheet, cancellationToken);
        return new OkObjectResult(result);                 // <3>
    }
    // </listing:fn-import>
}
