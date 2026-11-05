---
chapter: 13
part: "Part III — Real-World Integration Patterns"
title: "File Processing with Azure Blob Storage"
build-summary: "Partners drop property feeds as CSV, JSON or XML; within seconds Harbourline Connect validates every row, upserts the good ones into Dataverse, writes a report of the rest, and quarantines files it can't read."
builds:
  - "An Event Grid–sourced blob trigger, the only kind Flex Consumption supports"
  - "Streaming readers for CSV, JSON and XML with per-row error capture"
  - "A validating importer with duplicate detection and a CSV report"
versions: ".NET 10 · Storage Blobs extension 6.8.2 · CsvHelper 33.1.0 · System.Text.Json"
---

::: objectives
- Trigger a function from new blobs through Event Grid, and subscribe it correctly.
- Stream CSV, JSON and XML so memory stays flat as files grow.
- Validate each row and report problems by line number instead of failing the file.
- Write valid rows in batches, keyed for idempotency.
- Separate unreadable files into a quarantine container.
- Choose limits and designs for very large files.
:::

## The business problem

Harbourline works with three letting partners who send property listings every night. One sends CSV, one JSON, one XML; all three make mistakes: a rent of "TBC", a code with a lower-case letter, the same property twice. Today a coordinator opens each file, fixes what she can, and imports it by hand, two hours every morning.

The goal: drop the file, get the data, and get a report of what was rejected and why, which goes back to the partner.

## How it works

### Blob triggers on Flex Consumption

A **blob trigger** runs a function when a blob is added or changed in a container. On Flex Consumption it supports only the **Event Grid** source: Azure Storage publishes a `BlobCreated` event to Event Grid, which delivers it to the function's webhook endpoint. Detection takes seconds rather than the minutes that polling could take, and nothing scans the container.

The function declares `Source = BlobTriggerSource.EventGrid`. An **event subscription** on the storage account sends `BlobCreated` events for the container to `https://<app>.azurewebsites.net/runtime/webhooks/blobs?functionName=Host.Functions.<name>&code=<key>`, where the key is the app's `blobs_extension` system key.

### Streaming

Loading a 200 MB file into a string, then into a list, needs several times 200 MB of memory. **Streaming** reads one record at a time: `CsvHelper` reads rows from a `StreamReader`, `JsonSerializer.DeserializeAsyncEnumerable` yields array items one by one, and `XmlReader` moves forward through elements. Each reader in Harbourline Connect returns an `IAsyncEnumerable<FeedRow>`, so the importer processes rows as they arrive.

### Validate rows, not files

A file with 2,000 good rows and 3 bad ones should import 2,000 rows and report 3, not fail as a whole. Each `FeedRow` carries its line number, the parsed item or a parse error. The importer validates each item with the same `PropertyValidator` the API uses (Chapter 7), so a property is valid or not for the same reasons wherever it comes from.

![A dropped file raises a blob event; the function streams it, upserts the valid rows and always writes a report, while unreadable files go to quarantine.](figures/ch13/feeds.svg){#fig:feeds}

## Architecture

::: {#tbl:components caption="Components of the feed import"}
| Component | Role | Authenticates with | Failure path |
|---|---|---|---|
| `property-feeds` container | Landing zone | Partner: SAS or SFTP user, write-only | — |
| Event Grid subscription | Delivers `BlobCreated` | `blobs_extension` key in the URL | Event Grid retries delivery |
| `ImportPropertyFeed` | Reads, validates, imports | Managed identity (`FeedStorage__*`) | Unreadable → quarantine |
| `PropertyFeedImporter` | Validates, de-duplicates, batches | — | Row errors in the report |
| `feed-reports`, `feed-quarantine` | Outputs | Managed identity | — |
:::

## Prerequisites

- Chapter 7: `PropertyValidator` and `PropertyMapper`.
- Chapter 15 explains the `BulkWriter` used here; this chapter treats it as "upsert these rows in batches".
- A storage account with the containers created by the Bicep template in Chapter 20, or created by hand.

## Step-by-step implementation

### Step 1 – Read CSV as a stream

```{.csharp #lst:csv include="src/Harbourline.Connect.Core/Feeds/PropertyFeed.cs" region="feed-csv" caption="Reading CSV row by row, capturing bad rows"}
```

1. Partners leave out optional columns; a missing field is `null`, and the validator decides whether that's acceptable.
2. `ReadAsync` advances one row. Only that row is in memory.
3. A value that won't convert, such as `two` for bedrooms, becomes a row with a parse error and its line number, not an exception that stops the file.

Header matching is case-insensitive (`PrepareHeaderForMatch`), so `MonthlyRent`, `monthlyrent` and `MONTHLYRENT` all work.

### Step 2 – Read JSON and XML the same way

```{.csharp #lst:json include="src/Harbourline.Connect.Core/Feeds/PropertyFeed.cs" region="feed-json" caption="Streaming a JSON array"}
```

1. `DeserializeAsyncEnumerable` yields each element of a top-level array as soon as it is parsed.

The XML reader (in the same file) uses `XmlReader` with `DtdProcessing.Prohibit`. Prohibiting DTDs closes the door on XML external entity (XXE) attacks, where a crafted file makes the parser fetch local files or URLs.

### Step 3 – Validate, de-duplicate and import

```{.csharp #lst:import include="src/Harbourline.Connect.Core/Feeds/PropertyFeedImporter.cs" region="feed-import" caption="Validating every row and importing in buffered batches"}
```

1. A row is rejected for a parse error, a validation error or a duplicate code within the file. `UpsertMultiple` refuses a batch that contains the same key twice, so duplicates must be caught before sending.
2. Valid rows are buffered and written 1,000 at a time; memory stays bounded however long the file is.

Rows are upserted by property code: a property already in Dataverse is updated, a new one is created, and re-importing the same file changes nothing.

### Step 4 – Write the function

```{.csharp #lst:blobfn include="src/Harbourline.Connect.Functions/Feeds/PropertyFeedFunction.cs" region="blob-function" caption="The feed function: import, report, or quarantine"}
```

1. The Event Grid source, required on Flex Consumption.
2. The reader is chosen by file extension.
3. Every imported file gets a report blob in `feed-reports`, even when nothing was rejected; the partner always gets an answer.
4. A file that can't be read at all (an unknown extension, broken JSON, a malformed XML document) is copied to `feed-quarantine` for a person to look at.

The trigger binds the blob as `byte[]`, which is simple and keeps a copy for quarantine. That puts the whole file in memory, so Harbourline's partner contract caps feeds at 50 MB. Step 6 covers larger files.

### Step 5 – Subscribe the function to blob events

After deployment, create the event subscription. In the portal: storage account › **Events** › **+ Event Subscription**:

- **Event types:** Blob Created only
- **Endpoint type:** Web Hook; **Endpoint:** the URL above, with the `blobs_extension` key from **Function App › App keys › System keys**
- **Filters:** subject begins with `/blobServices/default/containers/property-feeds/`

Without the subject filter, files written to `feed-reports` would trigger the subscription too.

### Step 6 – Plan for very large files

For files of hundreds of megabytes or millions of rows, three changes apply. Bind the trigger to `Stream` instead of `byte[]`, as the meter-reading loader in Chapter 15 does. Split the work: a first function reads the file and enqueues chunks of, say, 10,000 rows (or byte ranges), and chunk functions import them in parallel. And checkpoint progress per file, so a restart resumes rather than repeats; the upsert keeps any repeat harmless. Durable Functions' fan-out/fan-in pattern (Chapter 12) is a natural fit for the split.

## Screens you will use

::: {.capture #fig:eg-sub caption="The event subscription delivers only BlobCreated events from the property-feeds container." app="Azure portal" path="Storage account › Events › Event Subscriptions › Create" annotations="1 Filter to event types: Blob Created; 2 Endpoint type: Web Hook; 3 Filters tab: subject begins with"}
:::

::: {.capture #fig:report caption="A report written next to the feed lists each rejected line and the reason." app="Azure portal" path="Storage account › Containers › feed-reports › <name>.report.csv › Edit" annotations="1 Line number; 2 Code; 3 Reason"}
:::

## Complete working code

- `src/Harbourline.Connect.Core/Feeds/PropertyFeed.cs`: item type and readers.
- `src/Harbourline.Connect.Core/Feeds/PropertyFeedImporter.cs`
- `src/Harbourline.Connect.Functions/Feeds/PropertyFeedFunction.cs`
- `tests/.../Feeds/PropertyFeedTests.cs`

## Configuration

::: {#tbl:settings caption="Storage settings for feeds"}
| Setting | Local | Azure | Secret? |
|---|---|---|---|
| `FeedStorage` | `UseDevelopmentStorage=true` | Not set (identity-based below) | — |
| `FeedStorage__blobServiceUri` | — | `https://<account>.blob.core.windows.net/` | No |
| `FeedStorage__queueServiceUri` | — | `https://<account>.queue.core.windows.net/` | No |
:::

The identity needs *Storage Blob Data Owner* (or *Contributor*) and *Storage Queue Data Contributor* on the account; the blob extension uses queues for poison-blob handling.

## Sample input and output

Input `partner-a-2026-10-27.csv` (fictional):

```text
code,name,city,postcode,bedrooms,monthlyRent,availableFrom
HBR-0101,3 Mill Lane,Leeds,LS9 8AA,2,950,2026-11-15
HBR-0102,5 Mill Lane,Leeds,LS9 8AA,two,980,2026-11-15
hbr-0103,7 Mill Lane,Leeds,LS9 8AA,3,1100,2026-12-01
HBR-0101,3 Mill Lane,Leeds,LS9 8AA,2,950,2026-11-15
```

Output `feed-reports/partner-a-2026-10-27.csv.report.csv`:

```text
line,code,reason
3,,"Unreadable row: The conversion cannot be performed."
4,"hbr-0103","code must look like ABC-1234."
5,"HBR-0101","Duplicate code in this feed."
```

And the log: `Feed partner-a-2026-10-27.csv: 4 read, 1 imported, 3 rejected`. (Illustrative; exact CsvHelper messages vary by version.)

## Testing

`PropertyFeedTests` reads small CSV, JSON and XML samples from memory and checks parsed values, bad-row capture, unsupported extensions, and the importer's duplicate and validation rejections, with Dataverse substituted. For the trigger, upload a file to Azurite with Azure Storage Explorer; locally, the Event Grid source needs a tunnel to receive events, so most teams test the importer directly and the trigger in a development subscription.

## Common errors and troubleshooting

::: {#tbl:errors caption="Errors you may see with file processing"}
| Symptom | Likely cause | Fix |
|---|---|---|
| Function never fires on Flex | Trigger without `Source = EventGrid`, or no subscription | Set the source; create the subscription |
| Subscription validation fails | Wrong URL or key | Use `/runtime/webhooks/blobs`, `Host.Functions.<name>`, `blobs_extension` key |
| Reports trigger more imports | Subscription without subject filter | Filter on the `property-feeds` container |
| Whole batch fails | Duplicate code in one `UpsertMultiple` | De-duplicate before writing |
| Out of memory on a large file | `byte[]` binding with a huge file | Bind to `Stream`; split into chunks |
| Same file processed twice | Event Grid redelivery or re-upload | Expected and harmless: upsert by key |
:::

## Security considerations

::: security
Treat every partner file as hostile input. Validate every field, prohibit DTDs in XML, cap file sizes, and never build queries or file paths from file contents. Give partners write-only access to their landing folder, never read or list access to other partners' files.
:::

## Performance and scalability

::: performance
Streaming keeps memory proportional to one batch, not the file. The Dataverse write is the slow part: batching with `UpsertMultiple` and modest parallelism (Chapter 15) matters more than parsing speed. Event Grid triggers scale out per file; several partners' files arriving together are processed in parallel.
:::

## Alternatives and design decisions

**Power Automate** can parse simple CSV files and suits low-volume, maker-owned imports. **Azure Data Factory** handles very large, scheduled, mapping-heavy loads with no code, and can write to Dataverse; Harbourline would choose it for a one-off migration of millions of rows, but chose code for nightly feeds because the validation rules already exist in C# and the report goes back to partners. **Dataverse's own data import** is designed for users importing spreadsheets, not for unattended feeds.

## Best practices

- Use the Event Grid source and filter subscriptions to one container.
- Stream input; never load a large file into a list.
- Validate each row with the same rules the API uses, and report by line number.
- De-duplicate keys before batch writes.
- Always write a report; quarantine files you can't read.
- Make imports idempotent with upsert by alternate key.

## Hands-on exercise

::: exercise
**Goal:** support a fourth partner's format.

**Steps:** partner D sends tab-separated files with a `.tsv` extension. Extend `PropertyFeedReader` to read them (CsvHelper's `Delimiter` setting), and add a unit test.

**Done when:** a `.tsv` file imports the same rows as the equivalent CSV, and the report format is unchanged.

**Stretch:** reject files larger than 50 MB before reading them, using the blob's length, and write a one-line report explaining why.
:::

## Knowledge check

::: knowledge-check
1. Which blob trigger source does Flex Consumption support, and what must you create for it to work?
2. Why does the importer reject duplicate codes within one file?
3. How do the readers keep memory flat as files grow?
4. What goes to `feed-reports`, and what goes to `feed-quarantine`?
5. Why is processing the same file twice harmless?
:::

Answers are in Appendix G.

::: summary
- On Flex Consumption, blob triggers use Event Grid: declare the source and create a filtered subscription.
- Streaming readers for CSV, JSON and XML turn files into rows with line numbers and errors.
- Rows are validated with the API's rules, de-duplicated and upserted in batches.
- Every file gets a report; unreadable files go to quarantine.
- Very large files need a `Stream` binding, chunking and checkpoints.
:::

## References

- [Tutorial: Trigger Azure Functions on blob containers using an event subscription](https://learn.microsoft.com/azure/azure-functions/functions-event-grid-blob-trigger) – Microsoft Learn, checked 2026-10-27.
- [Azure Blob storage trigger for Azure Functions](https://learn.microsoft.com/azure/azure-functions/functions-bindings-storage-blob-trigger) – Microsoft Learn, checked 2026-10-27.
- [How to serialize and deserialize JSON (IAsyncEnumerable)](https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/how-to) – Microsoft Learn, checked 2026-10-27.
- [XML external entity attacks and XmlReader security](https://learn.microsoft.com/dotnet/standard/data/xml/security-considerations) – Microsoft Learn, checked 2026-10-27.
- [Bulk operation messages](https://learn.microsoft.com/power-apps/developer/data-platform/bulk-operations) – Microsoft Learn, checked 2026-10-27.

<!-- Glossary terms introduced in this chapter: blob trigger, Event Grid, event subscription, streaming, quarantine, XXE -->
