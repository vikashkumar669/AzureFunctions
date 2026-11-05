---
chapter: 15
part: "Part IV — Advanced Enterprise Implementations"
title: "High-Volume Data Processing"
build-summary: "Harbourline loads a 100,000-row smart-meter export into Dataverse with bulk messages and controlled parallelism, isolates the bad rows without losing the good ones, and measures its own throughput with a method you can repeat."
builds:
  - "A bulk writer: UpsertMultiple batches, bounded parallelism, split-on-failure"
  - "A blob-triggered loader whose batch size and parallelism are app settings"
  - "A measurement method and a results template for your own environment"
versions: ".NET 10 · Dataverse.Client 1.2.27 · UpsertMultiple · Flex Consumption 2,048 MB"
---

::: objectives
- State Dataverse's service protection limits and how they are counted.
- Recognise throttling errors and the `Retry-After` value, and explain what `ServiceClient` does about them.
- Choose between `CreateMultiple`/`UpdateMultiple`/`UpsertMultiple`, `ExecuteMultiple` and single requests.
- Send batches in parallel with a bounded degree of parallelism.
- Isolate failing rows in all-or-nothing batches.
- Measure throughput honestly, and tune from measurements.
:::

## The business problem

Harbourline's properties have smart meters. Once a month the energy supplier sends an export of readings, about 100,000 rows, which must be in Dataverse before tenants' bills are prepared. A first attempt created one row per request, sequentially. It took most of a working day, and when it was run in parallel it started failing with errors nobody understood.

This chapter replaces that with a loader designed for volume, and shows how to find out how fast it really is in *your* environment.

## How it works

### Service protection limits

Dataverse protects itself and its other users with **service protection limits**. Microsoft documents three, evaluated **per user** (your application user counts as one) **per web server**, over a sliding **five-minute** window:

::: {#tbl:limits caption="Default service protection limits (Microsoft Learn)"}
| Measure | Limit per 5 minutes | Error when exceeded |
|---|---|---|
| Number of requests | 6,000 | `0x80072322` (-2147015902) |
| Combined execution time | 20 minutes (1,200,000 ms) | `0x80072321` (-2147015903) |
| Concurrent requests | 52 | `0x80072326` (-2147015898) |
:::

When a limit is exceeded, the Web API returns HTTP 429 with a `Retry-After` header; the SDK throws a fault whose `ErrorDetails` contains `Retry-After` as a `TimeSpan`. `ServiceClient` handles these errors itself: it waits for the `Retry-After` period and resends. Your code sees a throttling fault only when the client's own retries are exhausted.

The limits say something important about design. With one row per request, 6,000 requests in five minutes caps you at 20 rows a second, whatever you do. **Fewer, bigger requests** is the way through.

### Bulk messages

`CreateMultiple`, `UpdateMultiple` and `UpsertMultiple` write many rows of one table in a single request. Microsoft's guidance: on **standard tables**, start with 100 to 1,000 rows per request, and make the size configurable; on **elastic tables**, send 100 rows per request and run requests in parallel. Two behaviours matter:

- On standard tables a bulk request is a **transaction**: if one row fails, none is written.
- Not every standard table supports these messages; all elastic tables do. `UpsertMultiple` rejects a request that contains the same key twice.

`ExecuteMultiple` is the older alternative: it wraps up to 1,000 *different* requests, each processed separately, with an option to continue after errors. It saves round trips but not server work. For writing many rows of one table, the bulk messages are faster.

### Parallelism

Sending requests in parallel uses Dataverse's capacity better. Microsoft's guidance is to use the degree of parallelism the server recommends, returned in the `x-ms-dop-hint` header and exposed by `ServiceClient` as `RecommendedDegreesOfParallelism`, and to turn off the **affinity cookie** so requests spread over servers. The 52-concurrent-requests limit is the hard ceiling, and remember that every instance of your Function App adds to it.

![100,000 rows become 500 requests of 200 rows, sent a few at a time; a failed batch is split until the bad rows are found.](figures/ch15/bulk.svg){#fig:bulk}

## Architecture

::: {#tbl:components caption="Components of the bulk load"}
| Component | Role | Authenticates with | Failure path |
|---|---|---|---|
| `meter-readings` container | Landing zone for the export | — | — |
| `LoadMeterReadings` | Reads, maps, calls the writer, logs throughput | Managed identity | Throws when throttling outlasts `ServiceClient`'s retries |
| `BulkWriter` | Batches, parallelism, split-on-failure | — | Row errors in the result |
| `hl_meterreading` | Target, alternate key `hl_readingkey` | — | — |
:::

## Prerequisites

- Chapter 4: `hl_meterreading` and its key.
- Chapter 13: Event Grid blob triggers.
- A test export with the four columns shown under "Sample input and output"; generate fictional rows with any script.

## Step-by-step implementation

### Step 1 – Map rows with a natural key

```{.csharp #lst:meter include="src/Harbourline.Connect.Core/Bulk/MeterReadings.cs" region="meter-map" caption="Each reading is keyed by property, meter and time"}
```

1. The **natural key** (property, meter and timestamp) identifies a reading uniquely. Using it as an alternate key makes reloading the same file an update, not a duplicate.
2. The property lookup uses the property's alternate key; Dataverse resolves it.

### Step 2 – Write in parallel batches

```{.csharp #lst:upsert include="src/Harbourline.Connect.Core/Bulk/BulkWriter.cs" region="bulk-upsert" caption="Batching row indexes and writing batches in parallel"}
```

1. Batches carry *indexes* into the input list, so errors can point back to the original row.
2. `Parallel.ForEachAsync` runs at most `Parallelism` batches at once. The shared, thread-safe `ServiceClient` serves them all; asynchronous calls need no `Clone()`.

### Step 3 – Isolate bad rows

```{.csharp #lst:split include="src/Harbourline.Connect.Core/Bulk/BulkWriter.cs" region="bulk-split" caption="Splitting a failed batch to find the rows Dataverse rejects"}
```

1. `UpsertMultiple` needs the table's logical name on the collection.
2. Throttling faults are *not* caught here. `ServiceClient` has already retried them; if they still surface, the load is overrunning Dataverse and should stop, not split.
3. A batch of one that fails is a bad row: record its index, key and Dataverse's message.
4. Any other failure splits the batch in half and tries each half. A batch of 200 with one bad row costs about 16 extra requests to isolate it, instead of losing 199 good rows.

`BulkWriterTests.FailedBatch_IsSplitUntilTheBadRowIsFound` proves the behaviour with a substitute that fails any request containing one particular row.

### Step 4 – Make it tunable and observable

```{.csharp #lst:bulkfn include="src/Harbourline.Connect.Functions/Bulk/MeterReadingLoadFunction.cs" region="bulk-function" caption="The loader reads its tuning from settings and logs its throughput"}
```

1. Batch size and parallelism are app settings, with the server's recommended degree of parallelism as the default. You can tune in production without a deployment.
2. Every run logs rows, requests, settings, elapsed time and rows per second, so each run is also a measurement.

### Step 5 – Measure, don't guess

This book does not quote throughput figures. Throughput depends on your environment's capacity, the table's columns, plug-ins and flows on it, the row size, the region and the time of day; a number from someone else's environment would mislead you. Instead, measure with a method you can repeat:

1. Use a **sandbox** environment with production-like configuration: same plug-ins, flows, auditing and alternate keys.
2. Generate a fixed test file: 100,000 fictional rows, same file for every run.
3. Disable other load on the environment during the test.
4. For each setting combination, run twice and keep the second result (the first warms caches).
5. Record the log line from Step 4, and the count of throttling events from Application Insights.
6. Vary one setting at a time: batch size 100, 200, 500, 1,000 at fixed parallelism; then parallelism 1, 2, 4, 8 at the best batch size.

::: {#tbl:results caption="Results template: fill in from your own runs"}
| Run | Batch size | Parallelism | Elapsed (s) | Rows/s | Requests | Throttled | Failed rows |
|---|---|---|---|---|---|---|---|
| 1 | 100 | 4 | | | | | |
| 2 | 200 | 4 | | | | | |
| 3 | 500 | 4 | | | | | |
| 4 | 1,000 | 4 | | | | | |
| 5 | best | 1, 2, 8 | | | | | |
:::

Stop increasing parallelism when throughput stops rising or throttling appears. Then leave headroom: other integrations and users share the same environment.

## Deep dive: reading the service protection limits like an engineer

Service protection limits aren't a penalty; they're Dataverse telling you how much one user may ask of one server at a time. Treat them as a design input and a bulk load becomes predictable.

### The three limits, read closely

@tbl:limits lists the three limits. When one is exceeded, the fault carries its own error code: `0x80072322` for the number of requests, `0x80072321` for combined execution time, and `0x80072326` for concurrent requests. Logging the code tells you which limit you hit, and therefore which knob to turn.

All three apply **per user**, and an integration's application user is one user. They apply **per web server**: an environment has several, and requests are spread across them, so the effective ceiling for an environment can be higher than the table suggests. That's also why you can't calculate an exact maximum throughput, and why you must measure.

### What each limit tells you

- Hitting the **request count** means you're sending many small requests. Bulk messages (`UpsertMultiple`) turn 200 requests into one.
- Hitting the **execution time** means each request is expensive: big batches, synchronous plug-ins on the table, complex queries. Smaller batches or fewer plug-ins help; more parallelism makes it worse.
- Hitting **concurrency** means too many requests in flight at once. Lower `Parallelism` or the Function App's maximum instance count.

When a limit is exceeded, Dataverse returns the error with a `Retry-After` value (an `ErrorDetails` entry in the SDK, a `Retry-After` header in the Web API). Waiting that long and retrying is the correct response, and `ServiceClient` does it for you up to its retry settings. Retrying sooner just extends the throttle.

### The parallelism hint

`ServiceClient.RecommendedDegreesOfParallelism` returns a value from the server's response headers: Dataverse's own suggestion for how many requests to run in parallel. `LoadMeterReadings` uses it as the default for `Bulk:Parallelism`. It's a good starting point that adapts to the environment; override it only with measurements in hand.

### Batch size: the U-shaped curve

Microsoft's guidance for bulk messages on standard tables is to send **100 to 1,000 records** per request (and about 100 for elastic tables). Within that range, throughput typically follows a U-shape against batch size: tiny batches waste round trips, huge batches risk timeouts and make one bad row cost more (the split-on-failure logic in Step 3 re-sends the halves). Harbourline's default of 200 sits on the flat part of the curve for its tables. Your tables, with your plug-ins, will have their own curve; Step 5 explains how to measure it.

### Things that silently slow a bulk load

- **Synchronous plug-ins and real-time workflows** on the target table run for every row. Bulk messages trigger the `CreateMultiple`, `UpdateMultiple` or `UpsertMultiple` pipeline; plug-ins registered on single-row messages still run for each row.
- **Auditing** on the table adds a write per change.
- **Lookups by alternate key** cost a lookup each; they're still far cheaper than a query per row in your code.
- **Duplicate detection rules** are evaluated on create.

For one-off migrations, an administrator can ask Dataverse to skip custom synchronous and asynchronous logic with the `BypassBusinessLogicExecution` optional parameter. It needs a privilege that, by default, only System Administrators have, so the integration's normal least-privilege role can't use it. Use it in a migration window with an account created for that purpose, and switch it off afterwards.

::: warning
Bypassing business logic also bypasses the business rules you rely on. If a plug-in calculates a value, a bypassed load leaves that value empty. Agree, in writing, which logic may be skipped and how its results will be produced instead.
:::

## Screens you will use

::: {.capture #fig:bulk-logs caption="Each bulk run logs its settings and throughput." app="Azure portal" path="Application Insights › Logs (query from Chapter 18)" annotations="1 Batch size; 2 Parallelism; 3 Rows per second; 4 Failed rows"}
:::

::: {.capture #fig:api-analytics caption="Dataverse API usage analytics show request volume and throttling for the application user." app="Power Platform admin center" path="Manage › Analytics › Dataverse › API calls statistics" annotations="1 Requests by user; 2 Throttled requests"}
:::

## Complete working code

- `src/Harbourline.Connect.Core/Bulk/`: `BulkWriter.cs`, `MeterReadings.cs`
- `src/Harbourline.Connect.Core/Dataverse/DataverseErrors.cs`: error codes and `Retry-After`.
- `src/Harbourline.Connect.Functions/Bulk/MeterReadingLoadFunction.cs`
- `tests/.../Bulk/BulkWriterTests.cs`

## Configuration

::: {#tbl:settings caption="Bulk load settings"}
| Setting | Default | Range | Notes |
|---|---|---|---|
| `Bulk__BatchSize` | 200 | 1–1,000 | Lower for wide rows or tables with plug-ins |
| `Bulk__Parallelism` | `RecommendedDegreesOfParallelism` | 1–(52 ÷ instances) | Counts across all instances |
| Function App max instances | 40 (Bicep) | 1–1,000 | Multiply by parallelism |
:::

## Sample input and output

```text
propertyCode,meter,readAt,value
HBR-0042,ELEC-1,2026-10-01T00:00:00Z,15234.125
HBR-0042,GAS-1,2026-10-01T00:00:00Z,8812.400
```

```text
Bulk load readings-2026-10.csv: 100000 rows, 99998 ok, 2 failed,
  516 requests, batch 200, parallelism 4, <elapsed> s, <rate> rows/s
```

The two failures cost 16 extra requests to isolate. (Elapsed time and rate are left as placeholders on purpose: measure your own.)

## Testing

`BulkWriterTests` checks that rows are split into the configured batch size, that a failing row is isolated with its key and message, and that invalid options are refused. `DataverseErrorsTests` checks the throttling codes against the values published on Microsoft Learn and that `Retry-After` is read from the fault.

## Common errors and troubleshooting

::: {#tbl:errors caption="Errors you may see with high volume"}
| Symptom | Likely cause | Fix |
|---|---|---|
| Throttling fault (`0x80072322` etc.) reaches your code | Load exceeds limits even after client retries | Lower parallelism or instances; bigger batches |
| Whole batch rejected | One bad row in an all-or-nothing batch | Split-on-failure (Step 3) |
| `UpsertMultiple` error about duplicate keys | Same key twice in one request | De-duplicate before batching |
| Timeouts on large batches | Request too big or too slow (plug-ins) | Smaller batches |
| `UpsertMultiple` not supported for a table | Some standard tables don't support bulk messages | Use `ExecuteMultiple` or single requests |
| Fast in test, throttled in production | Other integrations share the user or environment | Measure in production-like conditions; leave headroom |
:::

## Security considerations

::: security
Bulk loads run as the integration's application user with write access to many rows; a bug writes many wrong rows quickly. Validate before writing, load into test first, and keep the import reversible: the alternate key lets you identify every row a load touched.
:::

## Performance and scalability

::: performance
The biggest levers, in order: fewer requests (bulk messages), then controlled parallelism, then lighter server work (fewer synchronous plug-ins on the table, fewer columns). Consider **elastic tables** for very high-volume, append-mostly data such as readings: they support bulk operations on all tables and scale differently, at the cost of transactions and some relational features.
:::

## Alternatives and design decisions

For one-off loads of millions of rows, Azure Data Factory or Dataflows can write to Dataverse without code. For continuous high-volume telemetry, ask whether Dataverse is the right store at all: readings could live in Azure (Data Explorer, a lakehouse) with only monthly totals written to Dataverse. Harbourline chose Dataverse for one month of readings because agents look at them alongside the tenant record.

## Best practices

- Design for the limits: per user, per server, five minutes, and shared by all your instances.
- Write with bulk messages, 100–1,000 rows per request on standard tables.
- Bound parallelism; start from the server's recommendation; turn off the affinity cookie for heavy parallel work.
- Isolate bad rows by splitting failed batches; never lose a good batch to one bad row.
- Make batch size and parallelism settings, and log both with every run.
- Measure in your environment and publish your own numbers, with the method.

## Guided lab: find your environment's sweet spot

This lab measures, rather than guesses, the best batch size and parallelism for loading meter readings into your development environment. **Requires:** the Function App deployed with `LoadMeterReadings`, an Event Grid subscription for the `meter-readings` container (as in Lab 13.3, with `functionName=Host.Functions.LoadMeterReadings`), and a few properties in Dataverse.

### Lab 15.1 – Generate test data

```bash
python3 - <<'EOF' > readings-20k.csv
import datetime as d
codes = ["HBR-0042", "HBR-0043", "HBR-0051"]
start = d.datetime(2026, 1, 1)
print("PropertyCode,Meter,ReadAt,Value")
for i in range(20000):
    t = start + d.timedelta(minutes=30 * i)
    print(f"{codes[i % 3]},ELEC-1,{t:%Y-%m-%dT%H:%M:%S},{100 + i * 0.25:.2f}")
EOF
wc -l readings-20k.csv
```

**Checkpoint:** 20,001 lines (header plus 20,000 readings), using property codes that exist in your environment.

### Lab 15.2 – Run the baseline

Set the batch size and let parallelism default to Dataverse's recommendation:

```bash
az functionapp config appsettings set -g <rg> -n <app> --settings Bulk__BatchSize=200
az functionapp config appsettings delete -g <rg> -n <app> --setting-names Bulk__Parallelism
az storage blob upload --auth-mode login --account-name <feed-storage> \
  --container-name meter-readings --name run-01.csv --file readings-20k.csv
```

When the run finishes, find its log line in Application Insights:

```kusto
traces
| where message startswith "Bulk load"
| project timestamp,
    batch = toint(customDimensions.BatchSize),
    parallel = toint(customDimensions.Parallelism),
    rows_s = todouble(customDimensions.RowsPerSecond),
    failed = toint(customDimensions.Failed),
    requests = toint(customDimensions.Requests)
| order by timestamp desc
```

**Checkpoint:** one row with 0 failed and about 100 requests (20,000 ÷ 200). Record the rows per second in the results table below.

### Lab 15.3 – Vary one thing at a time

Re-upload the same file under new names (`run-02.csv`, `run-03.csv`, …), changing **one** setting before each run. Because the readings are upserted by their natural key, every run writes the same 20,000 rows; nothing is duplicated.

| Run | Batch | Parallelism | Rows/s | Failed | Throttling errors seen? |
|---|---|---|---|---|---|
| 01 | 200 | (recommended: ___) | | | |
| 02 | 100 | (recommended) | | | |
| 03 | 500 | (recommended) | | | |
| 04 | 1000 | (recommended) | | | |
| 05 | best of 01–04 | 2 | | | |
| 06 | best of 01–04 | 8 | | | |
| 07 | best of 01–04 | 16 | | | |

Run each configuration at least twice and keep the median; single runs vary.

**Checkpoint:** a filled table. Most environments show a broad flat region and a drop at the extremes; the exact numbers belong to your environment and are worth keeping with your design notes.

### Lab 15.4 – Find the throttling point

Push parallelism higher (32, then 64) with your best batch size. Watch the run's dependencies in Application Insights for failures and the *API calls statistics* report shown in this chapter's screens for throttled requests.

**Checkpoint:** at some point throughput stops improving or falls, and throttling errors appear, even though the load still completes thanks to `Retry-After`. The configuration just below that point is your ceiling; production settings should sit comfortably below it, because other integrations and users share the same limits.

### Lab 15.5 – Break one row

Edit one line of the file to use a property code that doesn't exist (`HBR-9999`) and upload it again.

**Checkpoint:** the log shows *Batch of 200 rows failed; splitting to find bad rows*, the final result reports 1 failed and 19,999 ok, and the number of requests is only slightly higher than in a clean run. One bad row costs a few extra requests, not a failed load.

### Lab 15.6 – Write it down

Add a short paragraph to your team's design notes: the chosen batch size and parallelism, the date and environment of the measurements, and the throttling point. Next time someone asks "how fast can we load?", the answer is a link, not a guess.

## Hands-on exercise

::: exercise
**Goal:** run the measurement method.

**Steps:** generate a 20,000-row test file, load it into a sandbox with batch sizes 100, 200 and 500 at parallelism 4, and fill in the first rows of @tbl:results.

**Done when:** you can state, with your own numbers, which batch size is fastest in your environment and whether any run was throttled.

**Stretch:** make `hl_meterreading` an elastic table in a new sandbox and repeat with batch size 100 and higher parallelism. What changes?
:::

## Knowledge check

::: knowledge-check
1. What are the three service protection limits, and what is each counted per?
2. Why does one row per request cap throughput regardless of parallelism?
3. What happens on a standard table when one row in an `UpsertMultiple` request fails?
4. Why doesn't `BulkWriter` split batches that failed with throttling errors?
5. Why does this book not quote rows-per-second figures?
:::

Answers are in Appendix G.

::: summary
- Service protection limits count requests, execution time and concurrency per user, per server, per five minutes.
- Bulk messages turn thousands of requests into hundreds; on standard tables each is all-or-nothing.
- Bounded parallelism uses capacity without breaching the concurrency limit.
- Splitting failed batches isolates bad rows and keeps the good ones.
- Tune from your own measurements, with a repeatable method and settings you can change.
:::

## References

- [Service protection API limits](https://learn.microsoft.com/power-apps/developer/data-platform/api-limits) – Microsoft Learn, checked 2026-10-27.
- [Use bulk operation messages](https://learn.microsoft.com/power-apps/developer/data-platform/bulk-operations) – Microsoft Learn, checked 2026-10-27.
- [Send parallel requests](https://learn.microsoft.com/power-apps/developer/data-platform/send-parallel-requests) – Microsoft Learn, checked 2026-10-27.
- [Use ExecuteMultiple to improve performance for bulk data load](https://learn.microsoft.com/power-apps/developer/data-platform/org-service/execute-multiple-requests) – Microsoft Learn, checked 2026-10-27.
- [Elastic tables](https://learn.microsoft.com/power-apps/developer/data-platform/elastic-tables) – Microsoft Learn, checked 2026-10-27.
- [Web service error codes](https://learn.microsoft.com/power-apps/developer/data-platform/reference/web-service-error-codes) – Microsoft Learn, checked 2026-10-27.

<!-- Glossary terms introduced in this chapter: service protection limits, throttling, Retry-After, bulk operation message, ExecuteMultiple, degree of parallelism, affinity cookie, elastic table, natural key -->

<!-- expanded: deep dive + guided lab -->
