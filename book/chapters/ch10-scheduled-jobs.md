---
chapter: 10
part: "Part III — Real-World Integration Patterns"
title: "Scheduled Jobs and Background Processing"
build-summary: "Every night at 02:00 UTC, a timer function copies the invoices that changed in LedgerOne into Dataverse, picks up exactly where the last successful run stopped, and can run twice without creating a duplicate."
builds:
  - "A nightly invoice sync driven by a configurable timer"
  - "Checkpoints stored in Dataverse, with a watermark and an overlap window"
  - "A change-tracking reader for incremental loads out of Dataverse"
versions: ".NET 10 · Timer extension 4.3.1 · Dataverse.Client 1.2.27"
---

::: objectives
- Schedule work with timer triggers and NCRONTAB expressions, in UTC.
- Explain how the platform keeps one instance of a timer running, and what `IsPastDue` means.
- Load data incrementally with a watermark, and store checkpoints safely.
- Read only what changed in Dataverse with change tracking.
- Make scheduled work idempotent with upsert by alternate key.
:::

## The business problem

LedgerOne issues and updates invoices all day. Harbourline's lettings agents need to see in Dynamics 365 whether a tenant has paid, but they don't need it to the second: yesterday's position, every morning, is enough. The old job copied *every* invoice every night, took longer each month, and occasionally created duplicates when it was restarted halfway.

## How it works

### Timer triggers

A **timer trigger** runs a function on a schedule written as an **NCRONTAB** expression with six fields: second, minute, hour, day, month and day of week. `0 0 2 * * *` means 02:00:00 every day. On Flex Consumption, schedules are always in **UTC**; `WEBSITE_TIME_ZONE` isn't supported there.

The host keeps a timer **singleton**: however many instances the app has scaled to, only one runs a given timer function, and a new run doesn't start while the previous one is still running. If the host was down when a run was due, the next run reports `IsPastDue = true`.

### Incremental loads and watermarks

An **incremental load** copies only what changed since the last run. It needs a **watermark**: the point up to which data has been processed, such as a timestamp or a token. Three rules make watermarks safe (@fig:watermark):

1. **Take the new watermark before reading**, not after. Changes that arrive while the job runs are then picked up next time.
2. **Read with an overlap** of a few minutes, to cover clock differences between systems. The overlap re-reads some rows; upsert makes that harmless.
3. **Save the new watermark only when every row succeeded.** A failed row is then retried on the next run, instead of being skipped forever.

![Each run reads from the last saved watermark minus a five-minute overlap, and saves a new watermark only when every row succeeded.](figures/ch10/watermark.svg){#fig:watermark}

### Change tracking for Dataverse

When Dataverse is the *source*, it provides the watermark itself. With **change tracking** enabled on a table, `RetrieveEntityChangesRequest` returns rows created or updated since a **data token**, plus deleted rows, and a new token. Microsoft documents that tokens are valid for seven days by default, a value set by the organization's `ExpireChangeTrackingInDays`; after that, the request fails and you must start a full load again.

## Architecture

::: {#tbl:components caption="Components of the invoice sync"}
| Component | Role | Authenticates with | Failure path |
|---|---|---|---|
| `NightlyInvoiceSync` | Timer entry point | — | Throws when any row fails, so the run is logged as failed |
| `InvoiceSyncJob` | Reads, maps, upserts, checkpoints | — | Logs each failed invoice; keeps the old watermark |
| LedgerOne | Source of invoices | API key from Key Vault | Resilient client (Chapter 8) |
| `hl_syncstate` | Checkpoint per job | Managed identity | — |
| `hl_invoice` | Target, keyed by invoice number | Managed identity | Row fails if its lease doesn't exist |
:::

## Prerequisites

- Chapter 4: `hl_invoice`, `hl_lease` and `hl_syncstate` with their alternate keys.
- Chapter 8: the LedgerOne client and the mocks.

## Step-by-step implementation

### Step 1 – Store checkpoints in Dataverse

Each job has a row in `hl_syncstate`, keyed by name (@lst:checkpoint).

```{.csharp #lst:checkpoint include="src/Harbourline.Connect.Core/Sync/SyncStateStore.cs" region="checkpoint-store" caption="A checkpoint store backed by the hl_syncstate table"}
```

1. No row yet means "never run": the token is `null`.
2. The row is addressed by its alternate key, the job's name.
3. Upsert creates the row on the first run and updates it afterwards.

Why Dataverse rather than a blob or a table in storage? Checkpoints are business state: administrators can see them in a model-driven view, reset one by editing a field, and they are backed up with the data they describe.

### Step 2 – Map invoices with alternate keys

```{.csharp #lst:invmap include="src/Harbourline.Connect.Core/Sync/InvoiceMapper.cs" region="invoice-map" caption="Mapping a LedgerOne invoice to an upsertable row"}
```

1. The invoice number is the row's alternate key, so the same invoice always lands on the same row.
2. The lease lookup is set by *its* alternate key; Dataverse resolves it. No query for the lease's GUID is needed.
3. Unknown statuses fail loudly. Mapping them to a default would hide a new LedgerOne status until someone noticed wrong data.

### Step 3 – Write the job

```{.csharp #lst:sync include="src/Harbourline.Connect.Core/Sync/InvoiceSyncJob.cs" region="invoice-sync" caption="The incremental sync: watermark, overlap, upsert, checkpoint"}
```

1. The first run starts from the beginning of time; later runs start five minutes before the saved watermark.
2. The new watermark is taken *before* reading (rule 1).
3. A failed invoice is logged and counted, and the loop continues: one bad invoice must not block the other two thousand.
4. The watermark advances only when nothing failed (rule 3).

The job is idempotent by construction. Run it twice, or run it after a crash halfway through: each invoice is upserted by number, so the result is the same.

### Step 4 – Trigger it on a schedule

```{.csharp #lst:timer include="src/Harbourline.Connect.Functions/Jobs/InvoiceSyncFunction.cs" region="timer-function" caption="The timer function: configurable schedule, past-due logging, honest failure"}
```

1. `%InvoiceSyncSchedule%` reads the schedule from an app setting, so test can run hourly and production nightly, without a code change.
2. A past-due run is logged; the job itself doesn't care, because the watermark covers the gap.
3. Throwing when rows failed marks the invocation as failed in Application Insights, where an alert can fire (Chapter 18). Swallowing it would make a broken sync look healthy.

### Step 5 – Read Dataverse changes for the opposite direction

The customer sync in Chapter 21 pushes Dataverse account changes out. It reads them with change tracking (@lst:changes).

```{.csharp #lst:changes include="src/Harbourline.Connect.Core/Sync/ChangeReader.cs" region="change-reader" caption="Reading changed and deleted rows since a token"}
```

1. No token means a full load: every row is returned as new. Deletions are only reported once you have a token.
2. Each change is either a new or updated row, or a removed row's reference.
3. The token returned with the last page is the next run's starting point; save it with the checkpoint store.

### Step 6 – Run it now

To run a timer function on demand, call the host's admin endpoint. Locally:

```bash
curl -X POST http://localhost:7071/admin/functions/NightlyInvoiceSync \
  -H "Content-Type: application/json" -d "{}"
```

In Azure, the same endpoint needs the app's master key; the portal's **Test/Run** button does the same thing.

## Deep dive: what a timer trigger does and doesn't guarantee

Scheduled jobs fail in quiet ways: they run twice, they don't run at all, or they run at the wrong hour after a clock change. Knowing the timer trigger's guarantees lets you design for the gaps.

### One run at a time, across all instances

When a Function App runs on several instances, only **one** instance runs a given timer function at a time. The host coordinates this with a lock in the app's storage account (`AzureWebJobsStorage`). A new occurrence also won't start while the previous one is still running. You don't need your own "is the job already running?" flag for a single app.

Two situations break that assumption:

- **Two apps that share a storage account and host ID** share the lock, so only one of them runs its timers. Give every app its own storage account, as the Bicep template in this book does, or set distinct host IDs.
- **The same job in two places** (a timer and a manual run from the portal, or an old app that was never switched off) isn't coordinated. The checkpoint design below still keeps the data correct, but you pay twice for the work.

### Missed and late runs

With **schedule monitoring** on (the default for schedules of a minute or longer), the host records each occurrence in storage. If the app was stopped or redeployed at 02:00, the host notices the missed occurrence when it starts and runs the function once, with `TimerInfo.IsPastDue` set to `true`. `NightlyInvoiceSync` logs a warning in that case (*Invoice sync is running late*) so a late run is visible on the operations workbook. Missed occurrences aren't replayed one by one: ten missed runs become one late run. That's exactly right for a watermark-based job, which catches up from its checkpoint whatever the time.

### No retries

A timer function that throws isn't retried. The next attempt is the next scheduled occurrence. `NightlyInvoiceSync` turns that into a feature: when any invoice fails, it **doesn't advance the watermark** and throws, so the run shows as failed in monitoring, and tomorrow's run repeats today's window. If a day's delay is too long, schedule the job more often; the work per run shrinks accordingly.

### Time zones

NCRONTAB expressions are evaluated in **UTC** unless the app is configured otherwise, and support for changing the time zone depends on the hosting plan and operating system. Harbourline keeps every schedule in UTC on purpose: UTC has no daylight-saving jumps, so `0 0 2 * * *` never runs twice or not at all on the night the clocks change. When the business asks for "2 a.m. UK time", agree on 02:00 UTC instead, or accept that the job moves by an hour in summer.

### Reading NCRONTAB

Functions uses six fields, with seconds first: `{second} {minute} {hour} {day} {month} {day-of-week}`.

::: {#tbl:ncrontab caption="NCRONTAB examples"}
| Expression | Runs |
|---|---|
| `0 0 2 * * *` | Every day at 02:00:00 |
| `0 */5 * * * *` | Every five minutes, on the minute |
| `30 */5 * * * *` | Every five minutes, 30 seconds past the minute (Chapter 21's pull) |
| `0 0 6 * * 1-5` | 06:00 on weekdays |
| `0 0 1 1 * *` | 01:00 on the first day of each month |
| `0 15 2 * * 0` | 02:15 every Sunday |
:::

Keep schedules in app settings with the `%Name%` syntax (Step 4) when operations staff need to change them without a release, and in code when two schedules must stay aligned.

### Why the watermark overlaps

`InvoiceSyncJob` reads changes since the last watermark **minus five minutes**. Clocks in two systems are never perfectly aligned, and a record saved in LedgerOne just as the previous run started may carry a timestamp slightly before that run's start time. The overlap re-reads a few records each run; because the writes are upserts by alternate key, re-reading is harmless. Trading a little repeated work for never missing a change is the right deal for every watermark you build.

## Screens you will use

::: {.capture #fig:syncstate caption="Checkpoints are visible, and editable, in a model-driven view of hl_syncstate." app="Dynamics 365" path="Harbourline Admin app › Sync states" annotations="1 Job name; 2 Token (watermark); 3 Last run (UTC)"}
:::

::: {.capture #fig:timer-runs caption="Invocation history of the nightly sync." app="Azure portal" path="Function App › Functions › NightlyInvoiceSync › Invocations" annotations="1 Run at 02:00 UTC; 2 Success; 3 Duration"}
:::

## Complete working code

- `src/Harbourline.Connect.Core/Sync/`: `SyncStateStore.cs`, `InvoiceMapper.cs`, `InvoiceSyncJob.cs`, `ChangeReader.cs`
- `src/Harbourline.Connect.Functions/Jobs/InvoiceSyncFunction.cs`
- `tests/.../Sync/InvoiceSyncJobTests.cs`

## Configuration

::: {#tbl:settings caption="Settings for scheduled jobs"}
| Setting | Local | Azure | Secret? |
|---|---|---|---|
| `InvoiceSyncSchedule` | `0 */5 * * * *` (every 5 minutes, for testing) | `0 0 2 * * *` | No |
| `AzureWebJobs.NightlyInvoiceSync.Disabled` | `true` to stop it locally | Not set | No |
:::

## Sample input and output

A log line from a successful run:

```text
Invoice sync: 2143 upserted, 0 failed, watermark 2026-10-27T02:00:00.0412+00:00
```

And from a run with a bad row, followed by the failed invocation:

```text
Invoice INV-20931 could not be synchronised
  ObjectDoesNotExist: hl_lease With Ids = LSE-9999 Does Not Exist
System.InvalidOperationException: 1 invoices failed; the watermark was
  not advanced and the next run retries them.
```

(Illustrative output.)

## Testing

`InvoiceSyncJobTests` uses substitutes for LedgerOne, Dataverse and the checkpoint store, and a fixed `TimeProvider`:

```{.csharp #lst:test include="tests/Harbourline.Connect.UnitTests/Sync/InvoiceSyncJobTests.cs" region="test-watermark" caption="The watermark rules as a unit test"}
```

1. The job asked LedgerOne for changes since the old watermark minus the overlap.
2. It saved the new watermark, taken from the clock before reading.

A second test makes one upsert fail and checks that no checkpoint is saved.

## Common errors and troubleshooting

::: {#tbl:errors caption="Errors you may see with scheduled jobs"}
| Symptom | Likely cause | Fix |
|---|---|---|
| Job runs at the wrong hour | Schedule written in local time | Write it in UTC |
| `IsPastDue` every morning | App was stopped or deploying at 02:00 | Expected after outages; investigate if constant |
| Rows missing after a crash | Watermark saved before processing finished | Save only after success (rule 3) |
| Same failed row every night | Bad data upstream, e.g. unknown lease | Fix the data or the mapping; the log names the row |
| `RetrieveEntityChanges` fails after a holiday | Token older than the expiry (7 days by default) | Start again without a token: a full load |
| Duplicates after a rerun | Create instead of upsert | Upsert by alternate key |
:::

## Security considerations

::: security
Checkpoints control what gets processed. Only the integration's identity and administrators should be able to change `hl_syncstate`; leave it out of ordinary users' roles. A user who could reset a watermark could make the job re-send a month of data to a partner.
:::

## Performance and scalability

::: performance
A nightly job of a few thousand rows is fine with one upsert per row. Beyond that, batch: Chapter 15 replaces the loop with `UpsertMultiple` and parallel batches. Keep the run well inside the function timeout (30 minutes by default on Flex Consumption) or split the work into queued chunks.
:::

## Alternatives and design decisions

If LedgerOne could push events, Harbourline would prefer them: near-real-time and no polling. It can't, so a schedule is the honest choice. For Dataverse as a *source* at analytics scale, Azure Synapse Link or Microsoft Fabric link exports changes continuously without code; use change tracking when the target is an operational system that needs row-level control.

A timer could also start a Durable Functions orchestration that fans out the work (Chapter 12). Harbourline keeps the simple loop until the volume needs more.

## Best practices

- Write schedules in UTC and read them from app settings.
- Take the watermark before reading, read with an overlap, save only on full success.
- Make every write idempotent: upsert by alternate key.
- Keep checkpoints with the data, visible to administrators.
- Fail the invocation when rows fail, so monitoring sees it.
- Never enable `RunOnStartup` in production.

## Guided lab: run, break and recover the invoice sync

**Requires:** the host and mocks from Chapter 3, the schema and sample data from Chapter 4 (the mock's three invoices refer to the sample leases `LSE-0001` to `LSE-0003`), and the identity from Chapter 5.

### Lab 10.1 – The first run

Delete any existing checkpoint so the job starts from the beginning. Open the **Sync states** table (in the maker portal or a model-driven app) and delete the row named `ledgerone-invoices` if it exists. Then run the job on demand:

```bash
curl -i -X POST http://localhost:7071/admin/functions/NightlyInvoiceSync \
     -H "Content-Type: application/json" -d '{ "input": "" }'
```

**Expected log line** (illustrative):

```text
Invoice sync: 3 upserted, 0 failed, watermark 2026-10-27T09:41:07.5534120+00:00
```

**Checkpoint:** a `ledgerone-invoices` row now exists in **Sync states**, its token equal to the watermark in the log, and the invoices appear in Dynamics 365.

### Lab 10.2 – Run it again

Run the same command again immediately.

**Checkpoint:** the run succeeds and the watermark moves forward. The mock marks its three sample invoices as modified on every request, so they're upserted again; a real LedgerOne returns only the few records inside the five-minute overlap. Either way, no duplicate invoices appear in Dynamics 365, because `hl_invoicenumber` is an alternate key and every write is an upsert.

### Lab 10.3 – Make a run fail

Restart the mocks with a high failure rate (Chapter 8):

```bash
dotnet run --project samples/Harbourline.Mocks -- --FailureRate=0.95
```

Note the current token in **Sync states**, then run the job.

**Checkpoint:** the run fails (the host log shows the exception, and it counts as a failed invocation), and the token in **Sync states** is **unchanged**. A failed run never moves the watermark forward.

### Lab 10.4 – Recover

Restart the mocks with `--FailureRate=0` and run the job once more.

**Checkpoint:** the run succeeds and the watermark moves forward. Nothing was lost: the failed window was simply read again. This is the whole recovery procedure for a watermark job, which is why runbooks for these jobs are short.

### Lab 10.5 – Change the schedule without a release

In `local.settings.json`, set `"InvoiceSyncSchedule": "0 */2 * * * *"` and restart the host.

**Checkpoint:** the host log at start-up lists the next occurrences of the timer two minutes apart, and the job runs on its own. Set the value back to `0 0 2 * * *` afterwards. In Azure you'd change the app setting instead; the function picks the new schedule up when the app restarts.

### Lab 10.6 – Write your own schedules

Write NCRONTAB expressions for these requirements, then check them against @tbl:ncrontab:

1. Every 15 minutes during the working day, 08:00–17:45, Monday to Friday.
2. At 03:30 on the last day of… (careful: NCRONTAB has no "last day of month"; what would you do instead?)
3. Every hour, at five past the hour.

Suggested answers: `0 */15 8-17 * * 1-5`; schedule daily at 03:30 and check the date in code (`if (tomorrow.Day == 1)`); `0 5 * * * *`.

### Lab 10.7 – Read Dataverse changes (optional)

Step 5 reads Dataverse changes with change tracking for the opposite direction. Edit an account's phone number in Dynamics 365, then run `PushCustomersToLedgerOne` with the admin endpoint (replace the function name in the URL). **Checkpoint:** the log reports *Customer sync Dataverse → LedgerOne: 1 changed*, and the `accounts-to-ledgerone` checkpoint holds a new change-tracking token. Chapter 21 explains the complete two-way design.

## Hands-on exercise

::: exercise
**Goal:** add a second scheduled job.

**Steps:** add a timer that runs every 15 minutes, reads `hl_property` changes with `ChangeReader`, and logs the count of changed and deleted rows. Store its token in `hl_syncstate` under `properties-audit`.

**Done when:** editing a property in Dynamics 365 shows up as one change in the next run, and a second run with no edits reports zero.

**Stretch:** handle an expired token by starting a full load automatically and logging a warning.
:::

## Knowledge check

::: knowledge-check
1. What does `0 30 6 * * 1-5` mean, and in which time zone?
2. Why is the new watermark taken before the data is read?
3. What makes rereading the overlap window harmless?
4. What happens to a timer function that is still running when its next run is due?
5. Why does the timer function throw when some invoices failed?
:::

Answers are in Appendix G.

::: summary
- Timer triggers run on NCRONTAB schedules in UTC, as a singleton across instances.
- Incremental loads need a watermark: take it first, overlap the window, save only on success.
- Upsert by alternate key makes reruns and overlaps harmless.
- Change tracking gives Dataverse sources their own tokens, valid for seven days by default.
- Checkpoints in Dataverse are visible, auditable and backed up.
:::

## References

- [Timer trigger for Azure Functions](https://learn.microsoft.com/azure/azure-functions/functions-bindings-timer) – Microsoft Learn, checked 2026-10-27.
- [Use change tracking to synchronize data with external systems](https://learn.microsoft.com/power-apps/developer/data-platform/use-change-tracking-synchronize-data-external-systems) – Microsoft Learn, checked 2026-10-27.
- [Use Upsert to insert or update a record](https://learn.microsoft.com/power-apps/developer/data-platform/use-upsert-insert-update-record) – Microsoft Learn, checked 2026-10-27.
- [Manually run a non-HTTP-triggered function](https://learn.microsoft.com/azure/azure-functions/functions-manually-run-non-http) – Microsoft Learn, checked 2026-10-27.
- [Flex Consumption plan hosting](https://learn.microsoft.com/azure/azure-functions/flex-consumption-plan) – Microsoft Learn, checked 2026-10-27.

<!-- Glossary terms introduced in this chapter: timer trigger, NCRONTAB, singleton, incremental load, watermark, checkpoint, change tracking, data token -->

<!-- expanded: deep dive + guided lab -->
