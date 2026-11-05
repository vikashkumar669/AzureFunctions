---
chapter: 16
part: "Part IV — Advanced Enterprise Implementations"
title: "Data Migration and Reconciliation"
build-summary: "Harbourline's 40,000 properties move from the old lettings system into Dataverse with a mapping specification written as code, a migration that resumes after any failure, and a reconciliation report that proves the result."
builds:
  - "A mapping specification in code, one rule per spec row, with unit tests"
  - "A restartable migration that checkpoints after every batch"
  - "A reconciliation report: counts, missing and different rows, and checksums"
versions: ".NET 10 · Dataverse.Client 1.2.27 · CsvHelper 33.1.0 · Harbourline.Ops console"
---

::: objectives
- Plan a migration as extract, transform, load and reconcile, with a cut-over.
- Write a mapping specification and implement it so each rule is testable.
- Map lookups and choices, and decide what to do with values that don't map.
- Load with alternate keys and upsert, so reruns are safe.
- Make a long migration resumable with batch checkpoints.
- Prove a migration with counts, row fingerprints and checksums.
:::

## The business problem

Harbourline's old lettings system holds every property it has ever managed: about 40,000 rows, some twenty years old. Its data has quirks: town names in capitals, postcodes in any case, rents formatted with thousands separators, and a status code `V` that nobody remembers adding. Dynamics 365 must take over on a fixed weekend.

The business asks two questions: "Will everything come across?" and "How will we know?" A migration that can't answer the second is not finished.

## How it works

A migration has four stages (@fig:migration):

1. **Extract** the source data to a file or staging store, at a known point in time.
2. **Transform**: map each source row to the target's shape and validate it.
3. **Load** the mapped rows into Dataverse.
4. **Reconcile**: compare source and target and prove they match.

![Extract, map, load in checkpointed batches, then reconcile: counts, per-row fingerprints and one checksum prove the target matches the source.](figures/ch16/migration.svg){#fig:migration}

### The mapping specification

A **mapping specification** lists, for each target column, its source, the transformation and what happens when a value can't be mapped. It's written with the business before code, and signed off. @tbl:spec is Harbourline's.

::: {#tbl:spec caption="Mapping specification: legacy property → hl_property"}
| Target column | Source | Rule | If it can't be mapped |
|---|---|---|---|
| `hl_propertycode` (key) | `PROP_REF` | `HBR-` + 4-digit, zero-padded | — (always maps) |
| `hl_name` | `ADDRESS_LINE` | Trim spaces | — |
| `hl_city` | `TOWN` | Title case (`LEEDS` → `Leeds`) | — |
| `hl_postcode` | `POST_CODE` | Trim, upper case | — |
| `hl_bedrooms` | `BEDS` | Whole number | Reject row |
| `hl_monthlyrent` | `RENT_PCM` | UK number format (`1,250.00`) | Reject row |
| `hl_status` | `STATE` | `A`→Available, `L`→Let, `M`/`V`→Maintenance | Reject row (`X`, others) |
| `hl_availablefrom` | `AVAIL_DATE` | `dd/MM/yyyy`, date only | Leave empty |
:::

Two rules came from conversations, not data: the business confirmed that `V` ("void") means a property out of use, so Maintenance, and that `X` rows are demolished properties and must *not* be migrated. A mapping that guesses would have turned them into Available properties advertised on the portal.

### Lookups and choices

Choices map from source codes to the target's values, never by label text. Lookups are set by the related row's **alternate key** (`new EntityReference("hl_property", "hl_propertycode", "HBR-0042")`), so the migration needs no GUIDs, and the load order only has to put parent tables before child tables: properties before leases, leases before invoices.

### Restartable loads

A migration that dies at row 31,000 must not start again from row 1, and must not duplicate rows 1 to 31,000. Two techniques combine. **Upsert by alternate key** makes any repeat harmless. **Batch checkpoints** record the last completed batch, so a restart skips work already done.

### Reconciliation

**Reconciliation** compares source and target independently of the load code. Harbourline uses three measures:

- **Counts**: rows expected versus rows found.
- **Fingerprints**: a canonical string of the migrated fields for each row (`HBR-0042|12 Canal Wharf|Leeds|…`), compared key by key, to list missing and different rows.
- **Checksums**: a SHA-256 hash over all sorted fingerprints, one value per side. Equal checksums mean the whole set matches; the value goes in the sign-off document.

## Architecture

::: {#tbl:components caption="Components of the migration"}
| Component | Role | Authenticates with | Failure path |
|---|---|---|---|
| Legacy export (CSV) | Source, frozen at extract time | — | — |
| `Harbourline.Ops migrate` | Runs the migration from an operator's machine | Operator's sign-in (`az login`) | Stops; resumes from checkpoint |
| `LegacyMapping`, `MigrationRunner` | Transform and load | — | Rejected rows listed with reasons |
| `Reconciler` | Compares source and Dataverse | Operator's sign-in | Lists missing and different rows |
:::

A migration runs once, watched by a person, so Harbourline runs it as a console tool rather than a function: no timeout to manage, output on screen, and it uses the same Core code and tests as everything else.

## Prerequisites

- Chapter 4: the schema and alternate keys in the target environment.
- Chapter 15: `BulkWriter`.
- The operator's account has a role that can create and update `hl_property` and `hl_syncstate`.

## Step-by-step implementation

### Step 1 – Write the specification as code

```{.csharp #lst:map include="src/Harbourline.Connect.Core/Migration/LegacyMapping.cs" region="legacy-map" caption="The mapping specification, rule by rule"}
```

1. The property code rule from the first row of @tbl:spec.
2. The status rules, including the agreed meaning of `V`.
3. An unmappable status rejects the row with a reason instead of guessing.
4. The row is addressed by its alternate key, ready for upsert.

Each rule is a line a reviewer can check against the spec, and each has a test:

```{.csharp #lst:maptest include="tests/Harbourline.Connect.UnitTests/Migration/MigrationTests.cs" region="test-mapping" caption="One test checks every rule of the specification"}
```

### Step 2 – Load in checkpointed batches

```{.csharp #lst:run include="src/Harbourline.Connect.Core/Migration/MigrationRunner.cs" region="migration-run" caption="A migration that resumes after the last completed batch"}
```

1. The checkpoint holds the number of the last completed batch, or nothing for a fresh run.
2. The source is cut into numbered batches of 500 rows, always in the same order, so batch numbers mean the same thing on every run.
3. Completed batches are skipped.
4. The checkpoint is saved only after the batch's rows are written. A crash between write and save repeats one batch, which upsert makes harmless.

Run it:

```bash
export Dataverse__Url=https://harbourline-test.crm11.dynamics.com
dotnet run --project samples/Harbourline.Ops -- \
  migrate legacy-properties.csv properties-2026-11
```

The run name is the checkpoint's key. Use a new name for each rehearsal, and the same name to resume.

### Step 3 – Reconcile

```{.csharp #lst:fp include="src/Harbourline.Connect.Core/Migration/Reconciler.cs" region="fingerprint" caption="Fingerprints and the set checksum"}
```

1. Fields are joined with a separator in a fixed order. The fingerprint is computed the same way from the mapped source row and from the Dataverse row.
2. Numbers are formatted invariantly with two decimals; `1250` and `1250.00` must not differ.
3. Sorting makes the checksum independent of the order rows were read.

```{.csharp #lst:rec include="src/Harbourline.Connect.Core/Migration/Reconciler.cs" region="reconcile" caption="Comparing expected rows with what Dataverse holds"}
```

1. The target checksum covers only codes in the source, so properties created in Dataverse since go-live don't make the comparison fail.

```bash
dotnet run --project samples/Harbourline.Ops -- reconcile legacy-properties.csv
```

### Step 4 – Rehearse, then cut over

Run the whole migration at least twice in a test environment loaded with a recent copy of production. Time it, fix rejected rows at source or agree rules for them, and reconcile each time. On the cut-over weekend: freeze the old system, extract, migrate, reconcile, and switch users over only when the report says MATCH and the business has signed it.

## Screens you will use

::: {.capture #fig:migrate-run caption="A migration run resumed after an interruption." app="Terminal" path="dotnet run --project samples/Harbourline.Ops -- migrate …" annotations="1 Resumed after batch; 2 Migrated count; 3 Rejected rows with reasons"}
:::

::: {.capture #fig:reconcile caption="The reconciliation report: counts, differences and checksums." app="Terminal" path="dotnet run --project samples/Harbourline.Ops -- reconcile …" annotations="1 Source and target counts; 2 Missing and different; 3 Checksums; 4 MATCH"}
:::

## Complete working code

- `src/Harbourline.Connect.Core/Migration/`: `LegacyMapping.cs`, `MigrationRunner.cs`, `Reconciler.cs`
- `samples/Harbourline.Ops/Program.cs`: `migrate` and `reconcile` commands.
- `tests/.../Migration/MigrationTests.cs`

## Configuration

The Ops tool reads `Dataverse__Url` from the environment and signs in as the operator. The batch size (500) and bulk settings are constants in `MigrationRunner`; a migration is a project, and its settings are reviewed in code.

## Sample input and output

```text
PROP_REF,ADDRESS_LINE,TOWN,POST_CODE,BEDS,RENT_PCM,STATE,AVAIL_DATE
42,12 Canal Wharf ,LEEDS,ls11 5ps,2,"1,150.00",A,01/12/2026
43,Old Mill Cottage,YORK,yo1 7ex,3,"1,400.00",X,
```

```text
Source rows: 40213
Migrated:    40197
Resumed after batch: 31
  HBR-0043: Unknown state 'X'.
  …
Source 40197, target 40197
Missing:   0
Different: 0
Checksums: 3F9A1C27D0B4 / 3F9A1C27D0B4
MATCH
```

(Illustrative output with fictional data.)

## Testing

`MigrationTests` checks every mapping rule, the status table, the rejection of unknown states, the order-independence of the checksum, and resumption: with a checkpoint saying batch 1 is done, a run of 510 rows writes only the 10 rows of batch 2 and saves checkpoint 2.

## Common errors and troubleshooting

::: {#tbl:errors caption="Errors you may see during migration"}
| Symptom | Likely cause | Fix |
|---|---|---|
| Reconcile shows many "different" rows | Fingerprint formats differ (decimals, case) | Normalise both sides the same way |
| Rows for a lookup fail with "does not exist" | Parent table not migrated yet | Load parents first |
| Resume skips the wrong rows | Source file changed between runs | Freeze the extract; new run name for new data |
| Upserts fail right after deployment | Alternate key not active yet | Wait for the key to be Active |
| Numbers wrong by a factor of 1,000 | Culture-specific parsing | Parse with an explicit culture (`en-GB` here) |
:::

## Security considerations

::: security
Migration extracts contain all your data in one file. Keep them encrypted, in a restricted location, for as short a time as possible, and delete them after sign-off. Run the migration with a dedicated operator account whose elevated role is removed after cut-over.
:::

## Performance and scalability

::: performance
Migrations are bulk loads: the batch and parallelism advice of Chapter 15 applies. Disable non-essential plug-ins, flows and auditing on target tables during the load if the business agrees, and re-enable them afterwards; time the rehearsal both ways to see the difference.
:::

## Alternatives and design decisions

Azure Data Factory, Dataflows and the Configuration Migration tool can all move data into Dataverse. Harbourline chose code because the mapping rules needed tests, the same validation as the APIs, and an independent reconciliation that the business could sign. For a straightforward, one-table copy with clean data, a no-code tool is quicker.

## Best practices

- Write the mapping spec with the business; never guess unmappable values.
- Implement each spec rule as one line of code with a test.
- Load with upsert by alternate key; load parents before children.
- Checkpoint after each batch; resume with the same run name.
- Reconcile independently: counts, fingerprints, checksums.
- Rehearse at least twice, time it, and get sign-off on the report.

## Hands-on exercise

::: exercise
**Goal:** migrate leases.

**Steps:** write a mapping spec for a legacy lease export (lease number, property ref, start and end dates, rent, status), implement `LegacyLeaseMapping` with tests, and extend the Ops tool with `migrate-leases`.

**Done when:** a sample file loads with lease-to-property lookups set by property code, and a rerun changes nothing.

**Stretch:** extend the reconciler to leases and include the property code in each lease's fingerprint.
:::

## Knowledge check

::: knowledge-check
1. What are the four stages of a migration?
2. Why are `X` rows rejected instead of mapped to a default status?
3. How do upsert and checkpoints together make a migration restartable?
4. What does a fingerprint contain, and why are numbers formatted with two decimals?
5. Why is the target checksum computed only over codes present in the source?
:::

Answers are in Appendix G.

::: summary
- A migration is extract, transform, load and reconcile, rehearsed before cut-over.
- The mapping specification is agreed with the business and implemented rule by rule, with tests.
- Lookups by alternate key and upsert make loads order-simple and repeatable.
- Batch checkpoints let a stopped migration resume where it stopped.
- Counts, fingerprints and checksums prove the result, independently of the load code.
:::

## References

- [Use Upsert to insert or update a record](https://learn.microsoft.com/power-apps/developer/data-platform/use-upsert-insert-update-record) – Microsoft Learn, checked 2026-10-27.
- [Use an alternate key to reference a record](https://learn.microsoft.com/power-apps/developer/data-platform/use-alternate-key-reference-record) – Microsoft Learn, checked 2026-10-27.
- [Data management: configuration and data migration (Dynamics 365 implementation guide)](https://learn.microsoft.com/dynamics365/guidance/implementation-guide/data-management-configuration-data-migration) – Microsoft Learn, checked 2026-10-27.
- [Use bulk operation messages](https://learn.microsoft.com/power-apps/developer/data-platform/bulk-operations) – Microsoft Learn, checked 2026-10-27.

<!-- Glossary terms introduced in this chapter: mapping specification, extract, reconciliation, fingerprint, checksum, cut-over, rehearsal -->
