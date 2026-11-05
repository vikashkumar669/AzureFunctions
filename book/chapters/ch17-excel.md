---
chapter: 17
part: "Part IV — Advanced Enterprise Implementations"
title: "Excel Export and Import Automation"
build-summary: "Finance downloads a formatted monthly invoice workbook with one request, edits rents in a protected template, uploads it, and gets back exactly which cells were wrong while every valid change is applied."
builds:
  - "A formatted Excel export of Dataverse data with ClosedXML"
  - "A streaming writer for very large extracts with the Open XML SDK"
  - "A protected price template and an import with row-level error reporting"
versions: ".NET 10 · ClosedXML 0.105.1 (MIT) · DocumentFormat.OpenXml 3.5.1 (MIT)"
---

::: objectives
- Choose an Excel library for server-side code, with licensing in mind.
- Generate a formatted workbook from Dataverse with number formats, a table and frozen headers.
- Write very large worksheets with constant memory.
- Build an import template with protected cells and data validation.
- Read an uploaded workbook, validate every row and apply partial updates safely.
- Return Excel files from HTTP functions with the right headers and limits.
:::

## The business problem

Harbourline's finance team lives in Excel. Each month they ask IT for "the invoice spreadsheet", and each year they update hundreds of rents: today by emailing a spreadsheet to an administrator, who types the changes into Dynamics 365 over two days.

They want two buttons: *download this month's invoices*, and *upload new rents*. And when an upload has mistakes, they want to know which cells, not "import failed".

## How it works

### Choosing a library

An `.xlsx` file is a ZIP package of XML parts, defined by the Office Open XML standard. Server code can write it without Excel installed. @tbl:libs compares the main .NET options.

::: {#tbl:libs caption="Excel libraries for .NET server code"}
| Library | Licence | Strength | Weakness |
|---|---|---|---|
| **ClosedXML** (chosen) | MIT | Friendly API: cells, styles, tables, validation | Whole workbook in memory |
| **Open XML SDK** (chosen for large extracts) | MIT | Microsoft's low-level SDK; streaming writer | Verbose; you handle styles and shared strings |
| EPPlus | Commercial licence required for commercial use since version 5 | Rich features | Licence cost and terms |
| Excel interop / automation | — | — | Not supported on servers; don't use |
:::

Harbourline uses ClosedXML for formatted workbooks and the Open XML SDK's `OpenXmlWriter` for huge, plain extracts. Check any library's licence before it enters a product; a "free" library that requires a commercial licence becomes a compliance problem later.

### Partial success on import

A price upload with 300 rows and four mistakes should apply 296 changes and report four, by row and column. The import therefore runs in three passes: read and validate every row; check that every property code exists in Dataverse; write the valid changes in batches. Errors from every pass are merged into one list, sorted by row.

![Export builds a formatted workbook from Dataverse; import reads finance's edited template, applies valid rows and returns every bad cell.](figures/ch17/excel.svg){#fig:excel}

## Architecture

::: {#tbl:components caption="Components of the Excel automation"}
| Component | Role | Authenticates with | Failure path |
|---|---|---|---|
| `ExportInvoices` | GET a month's invoices as `.xlsx` | Function key / APIM | 400 for a bad month |
| `ImportPrices` | POST a price workbook | Function key / APIM | 413 too large; 400 not a workbook; 200 with errors |
| `FinanceService` | Queries invoices; applies prices | Managed identity | Row errors in the result |
| `FinanceWorkbook`, `PriceImport`, `StreamingWorkbook` | Build and read workbooks | — | — |
:::

## Prerequisites

- Chapter 7: outcomes and problem details.
- Chapter 15: `BulkWriter`, used to apply prices.

## Step-by-step implementation

### Step 1 – Query the data with labels

```{.csharp #lst:query include="src/Harbourline.Connect.Core/Excel/FinanceService.cs" region="finance-query" caption="Invoices for a month, with lease number and property name"}
```

1. A link to the lease brings the lease number and the property lookup in one query.
2. The linked lookup arrives as an `AliasedValue` wrapping an `EntityReference`, whose `Name` is the property's name.
3. The status's **formatted value** is the label users know ("Overdue"), which is right for a spreadsheet people read.

### Step 2 – Build a formatted workbook

```{.csharp #lst:export include="src/Harbourline.Connect.Core/Excel/FinanceWorkbook.cs" region="excel-export" caption="A finance workbook: title, headers, formats, table, frozen rows"}
```

1. A title row tells the reader what they are looking at, months after downloading it.
2. Amounts go in as numbers, never as formatted text, so finance can sum and sort them.
3. Number formats are applied to whole ranges; dates use ISO format to avoid day/month confusion.
4. An Excel **table** gives filter buttons and banded rows, and survives sorting.
5. Fixed column widths: automatic sizing measures fonts, which depend on what is installed on the server.

### Step 3 – Return it from a function

```{.csharp #lst:fnexport include="src/Harbourline.Connect.Functions/Excel/FinanceExcelFunctions.cs" region="fn-export" caption="GET /api/v1/finance/invoices/{month}"}
```

1. `FileStreamResult` sets the content type, and `FileDownloadName` sets `Content-Disposition`, so browsers save it as `invoices-2026-09.xlsx`.

The workbook is built in memory; for a month of invoices that is a few hundred kilobytes. Chapter 22 shows the alternative for large files: upload to Blob Storage and return a short-lived link.

### Step 4 – Stream very large extracts

For a plain extract of hundreds of thousands of rows, ClosedXML's in-memory model is too heavy. `OpenXmlWriter` writes XML elements straight to the package (@lst:stream).

```{.csharp #lst:stream include="src/Harbourline.Connect.Core/Excel/StreamingWorkbook.cs" region="excel-streaming" caption="Writing a worksheet element by element"}
```

1. The writer streams the worksheet part; rows are never held in a list.
2. Rows come from an `IEnumerable`, so they can come straight from a paged Dataverse query.
3. Numbers are written as numbers; everything else as **inline strings**, which avoids building a shared-string table in memory.

### Step 5 – Generate a protected template

Finance shouldn't edit codes or current rents, only new rents. The template locks everything except one column and validates it (@lst:template).

```{.csharp #lst:template include="src/Harbourline.Connect.Core/Excel/PriceImport.cs" region="excel-template" caption="A price template with data validation and protection"}
```

1. Excel itself refuses non-numbers and out-of-range values in the *New rent* column.
2. A fill colour shows where to type.
3. The sheet is protected, with only the yellow cells unlocked.

Sheet protection is guidance, not security: anyone can remove it, and the import validates everything again.

### Step 6 – Read and validate the upload

```{.csharp #lst:import include="src/Harbourline.Connect.Core/Excel/PriceImport.cs" region="excel-import" caption="Reading the price sheet, cell by cell"}
```

1. Skip the header row; read only used rows.
2. A row without a new rent is "no change", not an error.
3. Each problem is recorded with its row number and column letter, as finance sees them in Excel.

### Step 7 – Apply the valid changes

```{.csharp #lst:apply include="src/Harbourline.Connect.Core/Excel/FinanceService.cs" region="price-apply" caption="Checking codes exist, then updating rents in batches"}
```

1. Each change becomes a row with only the rent set, identified by its code: a partial update through `UpsertMultiple`. Codes were checked first, so no upsert creates a property.

```{.csharp #lst:fnimport include="src/Harbourline.Connect.Functions/Excel/FinanceExcelFunctions.cs" region="fn-import" caption="POST /api/v1/finance/prices"}
```

1. Uploads are capped at 5 MB, and a request without a length is refused.
2. A file that isn't a workbook is a 400, not a 500.
3. The response lists how many rents changed and every error. A 200 with errors is correct: the request succeeded, some rows didn't.

## Screens you will use

::: {.capture #fig:export-xlsx caption="The exported workbook: title, frozen header, table filters and number formats." app="Microsoft Excel" path="invoices-2026-09.xlsx" annotations="1 Title row; 2 Table header with filters; 3 Amount column formatted #,##0.00; 4 Frozen panes"}
:::

::: {.capture #fig:template-xlsx caption="The price template: only the yellow column is editable, and it accepts only valid rents." app="Microsoft Excel" path="prices-template.xlsx" annotations="1 Locked code and current rent; 2 Editable new rent; 3 Validation message on a bad value"}
:::

## Complete working code

- `src/Harbourline.Connect.Core/Excel/`: `FinanceWorkbook.cs`, `StreamingWorkbook.cs`, `PriceImport.cs`, `FinanceService.cs`
- `src/Harbourline.Connect.Functions/Excel/FinanceExcelFunctions.cs`
- `tests/.../Excel/ExcelTests.cs`

## Configuration

No settings. The upload limit (5 MB) and the rent range (from `PropertyValidator`) are constants, shared with the API so a rent valid in one place is valid in all.

## Sample input and output

```text
POST /api/v1/finance/prices
Content-Type: application/vnd.openxmlformats-officedocument.spreadsheetml.sheet
(body: prices-template.xlsx with 3 changes)

HTTP/1.1 200 OK
{"applied":2,"errors":[
  {"row":3,"column":"C","message":"New rent must be a number from 1 to 100000."}]}
```

## Testing

`ExcelTests` writes workbooks and reads them back with ClosedXML, so they test the real file format without Excel: the export has headers, numbers and formats; the template round-trips, with one valid change, one bad cell reported at row 3 column C, and an empty row ignored; the streaming writer produces a file another library reads; and a workbook without the expected sheet is reported, not thrown.

## Common errors and troubleshooting

::: {#tbl:errors caption="Errors you may see with Excel automation"}
| Symptom | Likely cause | Fix |
|---|---|---|
| Amounts can't be summed in Excel | Written as text | Write numbers; format with number formats |
| Dates appear as numbers | No date format on the range | Apply a date format |
| Font or sizing errors on Linux | Automatic column sizing measures installed fonts | Set widths explicitly |
| Out of memory on big exports | ClosedXML holds the workbook in memory | Stream with `OpenXmlWriter` |
| Upload returns 400 "Not an Excel workbook" | `.xls` or `.csv` uploaded | Accept `.xlsx` only, or convert first |
| Day and month swapped | Dates parsed as text with a culture | Read typed cell values, not text |
:::

## Security considerations

::: security
An uploaded workbook is untrusted input: cap its size, read only the cells you expect, never evaluate formulas or macros, and validate every value. When exporting, remember that a spreadsheet leaves your control the moment it is downloaded; export only the columns the user needs, and only rows they are allowed to see (Chapter 22).
:::

## Performance and scalability

::: performance
ClosedXML is fast enough for tens of thousands of rows per request; beyond that, stream. Keep exports out of the 230-second HTTP limit: for big or slow exports, generate in a queued function and return a download link when ready.
:::

## Alternatives and design decisions

Dynamics 365 itself can export views to Excel and import data from Excel, and for ad-hoc work that is often enough. Harbourline built its own because finance needed a fixed format every month, a template that blocks mistakes before upload, and row-level errors in their language. **Power Automate's Excel connector** works with files in OneDrive or SharePoint and suits small, maker-owned processes; it isn't designed for server-side generation of large or formatted workbooks.

## Best practices

- Check library licences before use; prefer MIT-licensed libraries for products.
- Write numbers and dates as typed values, with formats.
- Stream large extracts; keep formatted reports small.
- Give users protected templates with validation, and still validate on upload.
- Report errors by row and column; apply what is valid.
- Cap upload sizes and never trust workbook content.

## Hands-on exercise

::: exercise
**Goal:** add a "vacant properties" export.

**Steps:** add `GET /api/v1/reports/vacant.xlsx` that exports available properties with code, name, city, rent and available-from date, using `StreamingWorkbook` and a paged query.

**Done when:** the file opens in Excel with numbers and dates as typed values, and a unit test reads it back.

**Stretch:** add a second sheet with a summary by city, using ClosedXML, and decide which library to use when one workbook needs both.
:::

## Knowledge check

::: knowledge-check
1. Why does Harbourline avoid EPPlus, and what is never used on servers?
2. When do you choose `OpenXmlWriter` over ClosedXML?
3. Why is sheet protection not a security control?
4. Why does a price upload with errors return 200?
5. Which values are written as formatted labels, and which as raw values?
:::

Answers are in Appendix G.

::: summary
- ClosedXML builds formatted workbooks easily; the Open XML SDK streams very large ones; both are MIT-licensed.
- Write typed numbers and dates with formats; use formatted values for labels people read.
- Templates with protection and validation prevent most mistakes; the import still checks everything.
- Imports validate, check references, apply what's valid and report the rest by cell.
- Cap sizes, respect HTTP limits, and treat uploads as untrusted.
:::

## References

- [Open XML SDK for Office](https://learn.microsoft.com/office/open-xml/open-xml-sdk) – Microsoft Learn, checked 2026-10-27.
- [Considerations for server-side automation of Office](https://support.microsoft.com/help/257757) – Microsoft Support, checked 2026-10-27.
- [ClosedXML documentation](https://docs.closedxml.io/) – ClosedXML project, checked 2026-10-27.
- [EPPlus Software](https://epplussoftware.com/) – licence terms on the publisher's site, checked 2026-10-27.

<!-- Glossary terms introduced in this chapter: Office Open XML, ClosedXML, Open XML SDK, inline string, data validation, sheet protection -->
