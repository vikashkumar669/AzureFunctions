---
chapter: 21
part: "Part V — Capstone Projects"
title: "Project 1 — Customer and Contact Synchronization"
build-summary: "Accounts in Dynamics 365 and customers in LedgerOne stay in step in both directions, every five minutes, without loops, without lost updates, and with every conflict resolved by an agreed rule."
builds:
  - "Outbound sync from Dataverse change tracking, skipping echoes by hash"
  - "Inbound sync from a LedgerOne watermark with a tested conflict resolver"
  - "Loop prevention, idempotent writes and a reconciliation check"
versions: ".NET 10 · Timer extension 4.3.1 · Dataverse.Client 1.2.27 · Microsoft.Extensions.Http.Resilience"
---

::: objectives
- Design a two-way synchronization: direction, frequency, matching key, field ownership.
- Prevent sync loops with a stored hash of the synchronized fields.
- Resolve concurrent changes with explicit, tested rules.
- Combine change tracking (Dataverse) and a watermark (LedgerOne) in one design.
- Keep both directions retry-safe and observable.
:::

## The business problem

Customers are edited in two places. Lettings agents update company names and addresses in Dynamics 365; the finance team updates billing emails and phone numbers in LedgerOne. Chapter 11 sent Dataverse changes one way. Now finance's changes must come back, and when both sides change the same customer between syncs, nobody's work may be silently thrown away.

## How it works

### Design decisions first

Two-way sync goes wrong when its rules are implicit. Harbourline wrote these down before writing code:

::: {#tbl:design caption="Customer sync design decisions"}
| Question | Decision |
|---|---|
| Matching key | `account.accountnumber` = LedgerOne customer ID (alternate key on `accountnumber`) |
| Synchronized fields | Name, email, phone, city |
| Frequency | Every 5 minutes, each direction, offset by 30 seconds |
| Field ownership | Dataverse owns name and city; LedgerOne owns email and phone |
| New customers | Created in Dynamics 365 first; LedgerOne customers without an account are logged, not created |
| Deletes | Not synchronized; deactivation is a business process |
:::

### Loops and echoes

Every write the sync makes is itself a change. Without care, a Dataverse update pushed to LedgerOne shows up in LedgerOne's change feed, is pulled back, updates Dataverse, appears in change tracking, and so on forever. Harbourline breaks the loop with a **sync hash**: a short hash of the four synchronized fields, stored on the account in `hl_synchash` after every sync. A change whose fields hash to the stored value is an **echo** of our own write, and is skipped.

### Conflicts

The stored hash also detects **concurrent changes**. Compare each side's current hash with the stored "last synced" hash: if only one side differs, that side changed and wins. If both differ, both changed: the resolver merges them field by field, taking each field from its owner.

![Changes flow both ways every five minutes; a stored hash marks what was last synced, so echoes of our own writes are skipped and real conflicts are merged by field owner.](figures/ch21/sync.svg){#fig:sync}

## Architecture

::: {#tbl:components caption="Customer sync components"}
| Component | Role | Authenticates with | Failure path |
|---|---|---|---|
| `PushCustomersToLedgerOne` | Timer, every 5 min | — | Failed run; checkpoint not advanced |
| `PullCustomersFromLedgerOne` | Timer, every 5 min, +30 s | — | Same |
| `CustomerSyncService` | Both directions | Managed identity; LedgerOne key | Per-customer errors logged |
| `ConflictResolver` | Pure decision logic | — | — |
| `hl_syncstate` | Change-tracking token, watermark | — | — |
:::

## Prerequisites

Chapters 8 (LedgerOne client), 10 (change tracking, checkpoints) and 11 (the event-driven push this project complements). Enable change tracking on `account`, and add `hl_synchash`, `hl_lastsyncedfrom` and the `accountnumber` key with the setup tool.

## Step-by-step implementation

### Step 1 – Hash the synchronized fields

```{.csharp #lst:hash include="src/Harbourline.Connect.Core/Customers/CustomerSync.cs" region="sync-hash" caption="A hash of exactly the fields that are synchronized"}
```

1. Fields are joined with a control character that can't appear in data, after normalising whitespace and the case of email. "Same customer data" must hash the same, however it was typed.

### Step 2 – Write the conflict rules as a pure function

```{.csharp #lst:rules include="src/Harbourline.Connect.Core/Customers/CustomerSync.cs" region="conflict-rules" caption="Deciding what to do with a changed LedgerOne customer"}
```

1. Equal data needs nothing, whatever the timestamps say.
2. Each side is compared with the last synced state.
3. Both changed: merge by owner. The owners come straight from @tbl:design.

Because the resolver touches no services, every rule has a short test (@lst:mergetest).

```{.csharp #lst:mergetest include="tests/Harbourline.Connect.UnitTests/Customers/ConflictResolverTests.cs" region="test-merge" caption="The merge rule, as a test"}
```

### Step 3 – Push Dataverse changes out

```{.csharp #lst:push include="src/Harbourline.Connect.Core/Customers/CustomerSyncService.cs" region="sync-outbound" caption="Outbound: change tracking, echo check, upsert, stamp"}
```

1. Accounts without a LedgerOne ID aren't customers yet; echoes of the inbound sync's own writes are skipped by hash.
2. The idempotency key combines customer ID and hash: a retry of the same change is recognised, a later change is not.
3. The account is stamped with the new hash, so this write's own change-tracking entry becomes an echo next time.
4. The change-tracking token is saved after the batch.

### Step 4 – Pull LedgerOne changes in

```{.csharp #lst:pull include="src/Harbourline.Connect.Core/Customers/CustomerSyncService.cs" region="sync-inbound" caption="Inbound: watermark, match, decide, write both sides"}
```

1. Unmatched customers are logged for finance to resolve.
2. The resolver decides; this method only carries out the decision.
3. Dataverse is updated with the result and the new hash, recording that LedgerOne was the last source.

### Step 5 – Schedule both directions

```{.csharp #lst:timers include="src/Harbourline.Connect.Functions/Jobs/CustomerSyncFunctions.cs" region="sync-timers" caption="Two timers, offset so they don't run at the same moment"}
```

1. Push at :00, :05, :10…
2. Pull 30 seconds later, so the two directions rarely interleave on one customer.

### Step 6 – Reconcile weekly

Even good syncs drift: a manual fix, a failed run nobody noticed. Once a week, compare the hash of every matched pair on both sides and list differences, using the reconciliation pattern from Chapter 16. A small number of differences is information; a growing number is a bug.

## Deep dive: the edge cases that break two-way sync

The happy path of a sync is easy: something changes on one side, and it appears on the other. Real systems produce cases the design table at the start of this chapter doesn't mention. Harbourline listed them before go-live and agreed a behaviour for each.

::: {#tbl:edgecases caption="Edge cases and Harbourline's agreed behaviour"}
| Case | What happens without a rule | Harbourline's rule |
|---|---|---|
| Account merged in Dynamics 365 | The losing account is deactivated; LedgerOne keeps two customers | Merges are done by finance in both systems; the sync logs a warning for inactive accounts with a LedgerOne ID |
| Account number changed | Sync matches the wrong customer, or none | Account number is read-only on the form after creation; changes go through finance |
| Same customer created in both systems at once | Two records, one without a key | Creation only in Dynamics 365 (design table); LedgerOne-only customers are reported |
| Mass update (500 accounts re-assigned) | Hundreds of pushes, possible throttling | Only synced fields change the hash; re-assigning an owner isn't synced, so it's skipped |
| Mass update of a synced field | A spike of pushes | Acceptable; Chapter 15's batching applies if it's regular |
| Partner rejects one record | Whole batch fails, token not saved | Per-customer errors are logged and counted; the token advances; the reconciliation lists the failure |
| A field is cleared on one side | Empty value treated as "no change" | An empty value is a value; the hash includes it, so clearing is synced |
| Deactivation | Customer still active in LedgerOne | Not synced; deactivation is a business process (design table) |
:::

### The token-advance question

Should one failing customer stop the token from advancing? If it does, a single bad record blocks every later change for every customer, possibly for days. If it doesn't, the failed change is skipped until that customer changes again. Harbourline chose to advance and to **record** the failure: the per-customer error is logged with the customer ID, counted in a metric, and listed by the weekly reconciliation. The invoice sync in Chapter 10 made the opposite choice, because invoices are few and each one matters. Both are reasonable; what matters is that the choice is deliberate and visible.

### Observing a sync

Three numbers tell you whether a sync is healthy, and all three come from the code in this chapter:

- **Changes per run**, by direction. A sudden jump means a mass update or a bug; a long run of zeros on a busy day means the sync isn't seeing changes.
- **Decisions by type**: how many skips, one-sided updates and merges. Merges should be rare; if they're common, the two teams edit the same customers at the same time, and the business process may need a look.
- **Reconciliation differences** per week. Small and stable is normal; growing is a bug.

Put the first two on the operations workbook (Chapter 18) and the third in the weekly report to the data owner.

### When not to build a sync

Two-way sync is the most expensive integration pattern to run well. Before building one, ask whether a single owner per entity is possible (finance owns contact details, lettings owns everything else, and each system **reads** the other's data live), or whether a one-way sync plus an edit-in-place link would do. Harbourline built two-way sync because both teams genuinely needed to edit customers in their own tools; the edge cases above are the price.

## Screens you will use

::: {.capture #fig:account-sync caption="An account after synchronization: hash and last source are visible to administrators." app="Dynamics 365" path="Accounts › Northwind Lettings (fictional) › Sync section" annotations="1 Account number (LedgerOne ID); 2 hl_synchash; 3 Last synced from"}
:::

## Complete working code

`src/Harbourline.Connect.Core/Customers/`, `src/Harbourline.Connect.Functions/Jobs/CustomerSyncFunctions.cs`, `tests/.../Customers/ConflictResolverTests.cs`.

## Configuration

Uses the settings of Chapters 8 and 10. Schedules are constants here, because the two must stay offset; make them settings if your volumes need different intervals.

## Sample input and output

```text
Customer C-1001: Merge (Both changed; merged by field owner.)
Customer sync LedgerOne → Dataverse: 3 changed
Customer sync Dataverse → LedgerOne: 1 changed
```

## Testing

`ConflictResolverTests` covers equality, one-sided changes, the merge and hash normalisation. For the service, test against the mocks: change a customer in the mock and an account in Dataverse between two runs, and check the merged result on both sides. Then run twice more and check that nothing changes: the loop is closed.

## Common errors and troubleshooting

::: {#tbl:errors caption="Errors you may see with two-way sync"}
| Symptom | Likely cause | Fix |
|---|---|---|
| The same customer updates every 5 minutes | Hash not stamped, or fields normalised differently on each side | Stamp after every write; normalise in `Hash()` only |
| A finance edit is lost | Field owner rule wrong for that field | Fix the rule and its test |
| "No account has LedgerOne id" warnings | Customers created in LedgerOne first | Agree the creation process; create accounts |
| Push sees no changes | Change tracking not enabled on `account` | Enable it |
:::

## Security considerations

::: security
A sync job can change every customer in two systems. Limit its role to the columns it syncs (column security if needed), and alert when one run changes more than an expected number of customers, which is a sign of a bug or a bulk change upstream.
:::

## Performance and scalability

::: performance
Both directions read only changes, so cost follows activity, not customer count. If volumes grow, batch the Dataverse writes (Chapter 15) and move the push back to the event-driven path of Chapter 11, keeping the timer as a safety net.
:::

## Alternatives and design decisions

Last-writer-wins by timestamp is simpler, but depends on clocks in two systems and silently discards one side's edit. Dual-write and virtual tables avoid copying altogether, but need the other system's cooperation and connectors that LedgerOne doesn't offer.

## Best practices

- Write the sync rules down and agree them before coding.
- Match on a business key held by both systems.
- Stamp a hash of synced fields to skip echoes and detect conflicts.
- Keep conflict logic pure and tested; keep services as executors.
- Reconcile periodically; alert on unusual volumes.

## Guided lab: watch echoes and conflicts

**Requires:** change tracking enabled on `account`, the sample account `C-1001` (Chapter 4's `--sample-data`), the mocks and the Functions host running locally. You trigger each direction by hand with the admin endpoint:

```bash
run() { curl -s -o /dev/null -w "$1: %{http_code}\n" -X POST \
  http://localhost:7071/admin/functions/$1 -H "Content-Type: application/json" -d '{"input":""}'; }
```

### Lab 21.1 – First push

```bash
run PushCustomersToLedgerOne
curl -s http://localhost:5080/ledgerone/customers/C-1001 -H "x-api-key: local-development-key" | jq
```

**Checkpoint:** the host log reports *Customer sync Dataverse → LedgerOne: 1 changed* (or more, if other accounts have account numbers), and the mock now holds `C-1001` with the account's name, email, phone and city. In Dynamics 365, the account's **Sync hash** column is filled in.

### Lab 21.2 – Push again: nothing to do

```bash
run PushCustomersToLedgerOne
```

**Checkpoint:** *0 changed*. The push stamped the account with the hash, which created a change-tracking entry; this run saw that entry, found the hash unchanged, and skipped it as an echo of its own write.

### Lab 21.3 – A change in LedgerOne

LedgerOne owns phone numbers. Change the phone in the mock:

```bash
curl -s -X PUT http://localhost:5080/ledgerone/customers/C-1001 \
  -H "x-api-key: local-development-key" -H "Content-Type: application/json" \
  -d '{"customerId":"C-1001","name":"Northwind Lettings (fictional)",
       "email":"accounts@northwind-lettings.example","phone":"0113 496 0999",
       "city":"Leeds","modifiedUtc":"2026-01-01T00:00:00Z"}' > /dev/null
run PullCustomersFromLedgerOne
```

**Checkpoint:** the log shows a decision for `C-1001` of *UpdateDataverse*, and the account in Dynamics 365 shows the new phone number with **Last synced from** = LedgerOne. (The mock sets `modifiedUtc` itself, so the value you send is ignored.)

### Lab 21.4 – The echo goes nowhere

```bash
run PushCustomersToLedgerOne
```

**Checkpoint:** *0 changed*. The pull's write to Dataverse appeared in change tracking, but its hash matched the stored hash, so it wasn't sent back to LedgerOne. This is the loop the sync hash breaks.

### Lab 21.5 – A real conflict

Between two runs, change both sides:

1. In Dynamics 365, rename the account to *Northwind Lettings Ltd (fictional)*.
2. In the mock, change the email to `finance@northwind-lettings.example` (repeat Lab 21.3's `PUT` with the new email and the old name).

Then run the pull:

```bash
run PullCustomersFromLedgerOne
```

**Expected log line** (illustrative):

```text
Customer C-1001: Merge (Both changed; merged by field owner.)
```

**Checkpoint:** in Dynamics 365 the account keeps the **new name** (Dataverse owns names) and has the **new email** (LedgerOne owns emails). The pull also sent the merged record to LedgerOne, keyed by customer ID and the new hash; `GET` the customer from the mock to see the new name there too. Nobody's edit was lost.

### Lab 21.6 – Prove it's stable

Run pull and push twice more each. **Checkpoint:** every run reports zero changes. A sync that keeps finding work when nothing has changed has a normalisation or hashing bug; this one is quiet.

### Lab 21.7 – Write the rule for a new field

Following the chapter's exercise, decide the owner of the primary contact's email, add it to the design table, and predict the outcome of Lab 21.5 if both sides changed it. **Checkpoint:** your prediction follows from the owner rule alone, without reading the code.

## Hands-on exercise

::: exercise
**Goal:** add the primary contact's email to the sync, owned by LedgerOne.

**Done when:** a change in either system reaches the other once, the hash includes the new field, and the resolver's tests cover it.
:::

## Knowledge check

::: knowledge-check
1. What is an echo, and how does the hash detect it?
2. How does the resolver tell a one-sided change from a conflict?
3. Why are the two timers offset?
4. Why is the idempotency key customer ID plus hash?
:::

Answers are in Appendix G.

::: summary
- Two-way sync needs explicit rules: key, fields, frequency, owners, creation and deletion.
- A stored hash of synced fields prevents loops and reveals conflicts.
- A pure resolver merges conflicts by field owner, with a test per rule.
- Change tracking and watermarks feed the two directions; both are retry-safe.
:::

## References

- [Use change tracking to synchronize data with external systems](https://learn.microsoft.com/power-apps/developer/data-platform/use-change-tracking-synchronize-data-external-systems) – Microsoft Learn, checked 2026-10-27.
- [Dynamics 365 implementation guide](https://learn.microsoft.com/dynamics365/guidance/implementation-guide/overview) – Microsoft Learn, checked 2026-10-27.

<!-- Glossary terms introduced in this chapter: two-way synchronization, echo, sync hash, field ownership, conflict -->

<!-- expanded: deep dive + guided lab -->
