---
chapter: "J"
appendix: true
title: "Hands-on Exercises: Solution Guides"
---

Each chapter ends with a hands-on exercise. This appendix gives a solution guide for each one: an approach, the main steps, how to check your result, and the mistakes people commonly make. The guides deliberately stop short of complete code. Where a short code sketch helps, it's marked as a **sketch**: it shows the shape of a solution and the APIs to use, but, unlike the listings in the chapters, it isn't part of the compiled companion solution. Expect to adapt names and add the error handling the chapters describe.

## Chapter 1 – Introduction to Azure Functions and Dynamics 365

**Approach.** Treat it as a design workshop with yourself. For each requirement, answer the four questions in order and stop at the first one that decides the answer: must it block the save; can a copy of data be avoided; who owns and changes it; is it orchestration or code?

**Main steps.** Write each requirement as one sentence with a trigger and an outcome ("when a lease is signed, finance must receive the first invoice within an hour"). Fill in the decision table. For the stretch goal, a typical two-tool requirement is "reject a lease whose property is already let, and then tell the property manager": a synchronous plug-in validates and blocks; an asynchronous function or flow sends the notification.

**Check.** Ask a colleague to read only your table. If they need to ask "why?" about any row, the one-sentence explanation is missing a reason.

**Watch out for.** Choosing Functions because it's familiar. "We know C#" is a valid reason only when the requirement needs code; otherwise it's a maintenance cost passed to the next team.

## Chapter 2 – Azure Functions Architecture and Core Concepts

**Approach.** Edit the trigger gallery in `samples/Harbourline.Samples.Triggers/TriggerGallery.cs`, then reason about scale groups from Chapter 2's per-function scaling rules.

**Main steps.**

1. Change the timer expression to `0 30 6 * * 1-5` (06:30 UTC, Monday to Friday).
2. Add a second property to the output class with its own attribute, for example `[ServiceBusOutput("signature-audit", Connection = "ServiceBus")] public string? Audit { get; set; }`, and set it where the function builds its output.
3. List the functions: HTTP functions in the `http` group, the Event Grid–based Blob function in the `blob` group, and the timer and Service Bus functions each on their own instances (`function:<name>`).

**Check.** `dotnet build` succeeds; `func start` lists the timer with the new schedule (the host logs the next occurrences).

**Stretch.** At 2,048 MB the default HTTP concurrency is 16, so 20 instances × 16 = 320 concurrent executions. With two Dataverse calls per execution that's up to 640 concurrent calls from one application user, far above the concurrency limit in Chapter 15. Lower the maximum instance count, lower concurrency, or both.

## Chapter 3 – Development Environment Setup

**Approach.** Follow Lab 3 end to end, then cause one failure deliberately.

**Main steps.** Run the build and tests, start Azurite and the mocks, start the host and call `/api/health`. Then rename `local.settings.json` and start the host again.

**Check.** Without the file, the worker can't find required settings: the options validation from Chapter 6 stops start-up with a message naming the missing `Dataverse` settings. You should be able to say which line of `Program.cs` registered the validation that produced it.

**Stretch.** `az group update -n rg-hlconnect-dev --set tags.costCentre=<value>`. A sensible rule: every resource group that can incur cost must carry `costCentre`, enforced later with Azure Policy.

**Watch out for.** Committing `local.settings.json` while experimenting. Run `git status` before every commit.

## Chapter 4 – Understanding the Dataverse Web API and SDK

**Approach.** Extend the setup tool idempotently, then add the same filter to all three query styles in the probe.

**Main steps.** In `samples/Harbourline.Setup/Program.cs`, add an `EnsureColumnAsync` call for a Yes/No column. **Sketch:**

```csharp
await schema.EnsureColumnAsync("hl_property", new BooleanAttributeMetadata
{
    SchemaName = "hl_Furnished",
    DisplayName = L("Furnished"),
    RequiredLevel = None(),
    OptionSet = new BooleanOptionSetMetadata(
        new OptionMetadata(L("Yes"), 1), new OptionMetadata(L("No"), 0)),
});
```

In the probe, add `criteria.AddCondition("hl_furnished", ConditionOperator.Equal, true)` to the QueryExpression, `<condition attribute="hl_furnished" operator="eq" value="1" />` to the FetchXML, and `and hl_furnished eq true` to the Web API `$filter`.

**Check.** The second run of the setup tool reports that the column exists and creates nothing. All three queries return the same rows after you mark one sample property as furnished.

**Watch out for.** Forgetting to publish customizations before querying the new column, and comparing results from queries with different sort orders.

## Chapter 5 – Authentication and Secure Connectivity

**Approach.** Break one link of the identity chain at a time and read the error carefully before restoring it.

**Main steps and expected symptoms.**

- Remove the role from the application user: authentication still succeeds, but Dataverse refuses the read with a privilege error. The API returns a server error (Chapter 6 doesn't map it to a client error, deliberately; see Lab 19.2).
- Misspell `Dataverse__Url`: either start-up validation fails (if the value is no longer a valid URL) or token acquisition fails for an unknown resource.

**Check.** For each failure you can name the link (token, user, role, configuration) and the exact message that told you.

**Stretch.** A user-assigned identity needs its **client ID** in configuration, because a Function App can have several user-assigned identities and the credential must know which one to use. With `DefaultAzureCredential`, set `AZURE_CLIENT_ID`; with `ManagedIdentityCredential`, pass the client ID explicitly.

## Chapter 6 – Your First Azure Function Connected to Dataverse

**Approach.** Follow the data from Dataverse to JSON: column, mapping, contract, test.

**Main steps.**

1. Add `bool Furnished` to `PropertyDto` (as a new optional member at the end, so existing callers aren't affected).
2. Add `"hl_furnished"` to the columns retrieved by `PropertyService` and to `PropertyTable` in `Schema.cs`.
3. Map it in `PropertyMapper` with `entity.GetAttributeValue<bool>("hl_furnished")`, which returns `false` when the column is empty.
4. Add a mapper test with a furnished and an unfurnished entity.

**Check.** `GET /api/v1/properties/HBR-0042` returns `"furnished": true` after you set the column in Dynamics 365.

**Stretch.** A custom `ValidationAttribute` on `DataverseOptions.Url` that rejects non-HTTPS URIs, combined with `ValidateDataAnnotations().ValidateOnStart()`, stops the host at start-up.

**Watch out for.** Changing the order of constructor parameters in a positional record that other code constructs; add new members at the end.

## Chapter 7 – CRUD APIs for Dynamics 365

**Approach.** Sorting changes both the query and the meaning of a continuation token, so it must be part of the token's filter.

**Main steps.**

1. Add `Sort` to `PropertyQuery` and read `sort` in the list function; reject values other than `name` and `rent` with a validation error.
2. In the service, add the chosen order (`hl_name` or `hl_monthlyrent`) followed by the ID as a tie-breaker.
3. Include the sort in `ContinuationToken.FilterOf`, so a token issued for one sort order fails `Decode` for another.

**Check.** A test asserts that the `QueryExpression` sent to the substitute has the expected `Orders`. A second test encodes a token for `sort=name` and expects a `FormatException` when decoding it with the `sort=rent` filter.

**Stretch.** A soft delete (`DELETE` setting status to *Maintenance*) fits Harbourline because properties have history (leases, invoices, meter readings) that must keep its lookups, and because the integration role isn't allowed to delete.

## Chapter 8 – Integrating External REST APIs

**Approach.** Make the operation idempotent first; only then is it safe to turn retries back on.

**Main steps.**

1. In the SignBridge mock, keep a dictionary from idempotency key to envelope, as the LedgerOne order endpoint does, and return the existing envelope for a repeated key.
2. In `SignBridgeClient.CreateEnvelopeAsync`, add the header `Idempotency-Key: <lease number>` to the request.
3. Remove `DisableForUnsafeHttpMethods()` from the SignBridge registration, with a comment explaining why it's now safe.

**Check.** With `--FailureRate=0.5`, call the activity (or a small console harness) ten times for one lease; the mock holds one envelope.

**Stretch.** `AddStandardHedgingHandler` sends a second request when the first is slow and uses whichever answers first. Use it only for safe, idempotent reads, and compare p95 and p99 dependency durations before and after.

**Watch out for.** Using a random GUID as the key. A key must identify the **business operation**, so a retry produces the same key.

## Chapter 9 – Webhooks, Plug-ins, and Power Automate Integration

**Approach.** Register a second step on the same webhook endpoint, and route by message and table in the function.

**Main steps.** In the Plug-in Registration Tool, add a step on `Create` of `hl_invoice`, asynchronous, with the same webhook. In the function, branch on `evt.PrimaryEntityName` and `evt.MessageName`: for invoices, read `hl_invoicenumber` and `hl_amount` from the target (a money value arrives as an object with a `Value`).

**Check.** Creating an invoice logs both values; updating one doesn't call the function, because no step is registered on `Update` of `hl_invoice`.

**Stretch.** Return `503` when sending to the queue throws. Dataverse retries once for 502, 503 and 504, and the System Job shows the outcome.

**Watch out for.** Growing one webhook function into a switch over many tables. Two or three routes are fine; beyond that, give each event its own function and endpoint.

## Chapter 10 – Scheduled Jobs and Background Processing

**Approach.** Reuse `ChangeReader` and the checkpoint store; the new job is mostly wiring.

**Main steps.** Add a timer function with `[TimerTrigger("0 */15 * * * *")]`. Load the checkpoint named `properties-audit`, call `ChangeReader.ReadAsync` for `hl_property`, log the counts of new or updated and deleted rows, and save the new token.

**Check.** Edit one property; the next run reports one change. Run again without edits; it reports zero.

**Stretch.** When Dataverse reports that the change-tracking token has expired, start again without a token (a full read), log a warning, and save the new token from that read.

**Watch out for.** Saving the token before processing the changes. Always save **after**, so a failed run repeats rather than skips.

## Chapter 11 – Azure Service Bus and Event-Driven Architecture

**Approach.** A second subscription gives the audit function its own copy of every message, with its own delivery count and dead-letter queue.

**Main steps.** In `infra/main.bicep`, add a sibling of the `ledgerone` subscription named `audit`. Add a function with `[ServiceBusTrigger("account-events", "audit", Connection = "ServiceBus")]` that parses the event and logs `PrimaryEntityName`, `PrimaryEntityId` and `CorrelationId`.

**Check.** One account change produces two invocations, one per subscription. With the LedgerOne mock stopped, the audit function still completes its messages.

**Stretch.** Replace the subscription's default rule with a SQL filter on the request-name application property that Dataverse sets (copy the exact property name from Service Bus Explorer; it contains slashes, so quote it in the filter expression).

## Chapter 12 – Durable Functions and Long-Running Workflows

**Approach.** Add one activity call and one timer to the orchestrator's expiry path, keeping it deterministic.

**Main steps.**

1. Add an activity `CreateFollowUpTask` that creates a Dataverse `task` regarding the lease and owned by the lease's owner.
2. In the orchestrator, when `reminder == MaxReminders`, call the activity, then `await context.CreateTimer(context.CurrentUtcDateTime.AddDays(1), CancellationToken.None)`, then wait for the signature once more before expiring.

**Check.** With `ReminderEvery` shortened temporarily, an unanswered workflow creates exactly one task and then completes with `"Expired"`.

**Stretch.** Wait for `Task.WhenAny(signed, cancelled, due)`, where `cancelled` is `context.WaitForExternalEvent<string>("Cancelled")`, raised by a webhook from a Dynamics 365 button.

**Watch out for.** Changing the orchestrator while instances are running. Follow the versioning advice in Chapter 12's deep dive.

## Chapter 13 – File Processing with Azure Blob Storage

**Approach.** TSV is CSV with a different delimiter; reuse the CSV reader.

**Main steps.** Add a parameter for the delimiter to `ReadCsvAsync` (default `","`), set `Delimiter = delimiter` in the `CsvConfiguration`, and add `".TSV" => ReadCsvAsync(content, "\t", ct)` to `PropertyFeedReader.ReadAsync`.

**Check.** A unit test feeds the same rows as CSV and TSV and expects identical `FeedRow` values.

**Stretch.** Check the blob's length before reading (the trigger can bind to the blob's properties, or use the Blob SDK) and write a report with a single line: `0,,File is larger than 50 MB.`

## Chapter 14 – Building a Secure Integration API

**Approach.** Authorize per operation in API Management.

**Main steps.** Create the client app and assign only `Properties.Read`. Create its subscription. Move the `required-claims` check from the API-level policy into each operation's policy: `Properties.Read` for search, `Orders.Submit` for order submission.

**Check.** The read-only partner gets 200 from search and **401** from `POST /v1/orders` at the gateway; no invocation appears in the Function App.

**Stretch.** A `quota` policy (calls per renewal period) on the read-only product. When it runs out the gateway answers **403** with a message about the quota. Decide whether partners get advance warning, and document the limit in the product description.

## Chapter 15 – High-Volume Data Processing

**Approach.** Follow Lab 15 exactly and fill in the chapter's results table.

**Check.** You can state your environment's fastest batch size, with a median of at least two runs per configuration, and whether any run was throttled.

**Stretch.** Elastic tables don't use transactions for bulk operations, so Microsoft recommends about 100 records per request and more parallel requests. Expect throughput to rise with parallelism further than for standard tables, and expect partial success within a batch.

**Watch out for.** Comparing runs while other people use the sandbox, and changing two settings at once.

## Chapter 16 – Data Migration and Reconciliation

**Approach.** Copy the property migration's structure: a mapping specification, a pure mapping class with tests, the same runner and reconciler.

**Main steps.** Define the lease mapping: legacy lease number to `hl_leasenumber`, property ref to an `EntityReference` by `hl_propertycode` (using `LegacyMapping.CodeOf`), dates parsed with the UK culture, rent with `NumberStyles.Number`, and status codes to `LeaseStatus`. Write tests for each rule and each rejection. Add a `migrate-leases` case to the Ops tool.

**Check.** A sample file loads with the property lookups set; running it again under a new run name changes nothing.

**Stretch.** A lease fingerprint includes the property code from the lookup, so reconciliation catches a lease attached to the wrong property.

## Chapter 17 – Excel Export and Import Automation

**Approach.** Use the streaming writer for the rows and a paged query for the data.

**Main steps.** Add an HTTP function at `v1/reports/vacant.xlsx`. Query available properties page by page, write each row with `StreamingWorkbook` (numbers as numbers, dates as dates), and return a `FileStreamResult`.

**Check.** The file opens in Excel with right-aligned numbers and real dates; a unit test reads it back with ClosedXML and checks one row.

**Stretch.** Summaries are small; build them with ClosedXML. If one workbook needs both a huge sheet and a formatted summary, write the large sheet with the streaming writer and keep the summary simple, or produce two files.

## Chapter 18 – Logging, Monitoring, and Troubleshooting

**Approach.** Tag spans with the business key, then query by tag.

**Main steps.** In each lease activity and in the webhook, start an activity from `HarbourlineTelemetry.Source` and call `SetTag("lease.number", leaseNumber)`. **Sketch** of the query:

```kusto
union requests, dependencies
| where customDimensions["lease.number"] == "LSE-0042"
| project timestamp, itemType, name, operation_Id
| order by timestamp asc
```

**Check.** For one test lease, the result shows the webhook request, the queue processing, the orchestration's activities and the callback.

**Stretch.** In a workbook, add a text parameter `LeaseNumber` and reference it in the query as `{LeaseNumber}`.

## Chapter 19 – Automated Testing and Code Quality

**Approach.** Lab 19.2 is the first test. For the second, call `ContinuationToken.Decode` with a token that encodes page 1.

**Main steps.** Encode `new PagePosition(1, null, filter)` with `ContinuationToken.Encode`, then assert that `Decode(token, filter)` throws `FormatException`.

**Check.** Comment out the `position.Page < 2` condition: the test fails. Restore it.

**Stretch.** Substitute `IOrganizationServiceAsync2` and `ILedgerOneClient`, return an account whose stored `hl_synchash` equals the hash of its fields, and assert `ledger.DidNotReceive().UpsertCustomerAsync(...)`.

## Chapter 20 – Deployment and DevOps

**Approach.** A readiness endpoint touches the dependency that most often breaks after deployment: Dataverse access.

**Main steps.** Add `GET /api/health/ready` that reads one row from `hl_syncstate` (or calls `WhoAmI`) and returns 200, or 503 with a problem body on failure. Exclude the path from Easy Auth if your smoke test calls it anonymously, or give the smoke test a token. Call it in the workflow after deployment.

**Check.** Deploy to an environment where the application user doesn't exist: the workflow fails at the smoke-test step.

**Stretch.** Add a step running `az deployment group what-if` before the infrastructure step and append its output to `$GITHUB_STEP_SUMMARY`.

## Chapter 21 – Project 1: Customer and Contact Synchronization

**Approach.** A new synced field touches four places: the fields record, the hash, the owner rule and the mapping on both sides.

**Main steps.** Add the email to `CustomerFields`, include it (normalised like the account email) in `Hash()`, give it to LedgerOne in the merge rule, read it from the primary contact on the Dataverse side, and add it to the LedgerOne customer model and mock. Add resolver tests for one-sided changes and a merge.

**Check.** Labs 21.3 to 21.6 behave the same way for the new field.

**Watch out for.** Forgetting that adding a field changes every stored hash: the first run after deployment sees every customer as changed once. Plan that run, or recompute and stamp hashes first.

## Chapter 22 – Project 2: Secure Excel Export API

**Approach.** One structured log line per download, with the fields as named properties.

**Main steps.** Add a `[LoggerMessage]` method such as `"Lease pack {LeaseNumber} delivered to {CallerObjectId} as {DeliveryMethod}"`, called in both the direct and the link branch.

**Check.** Sketch of the query:

```kusto
traces
| where message startswith "Lease pack"
| summarize downloads = count()
    by agent = tostring(customDimensions.CallerObjectId), bin(timestamp, 1d)
```

**Watch out for.** Logging names or email addresses. The object ID is enough to identify the user in Entra ID when needed.

## Chapter 23 – Project 3: High-Volume Property Search API

**Approach.** The cursor must contain every sort key, including the tie-breaker.

**Main steps.** Add `Rent` to `SearchCursor`. Order by rent, then ID. The seek condition becomes "rent greater than the last rent, **or** rent equal to it and ID greater than the last ID", built as an `or` filter in the query.

**Check.** A test with five properties of which three share the same rent, paged two at a time, returns each property exactly once.

**Watch out for.** Cached first pages: add the sort to the cache key.

## Chapter 24 – Project 4: Event-Driven Order Processing

**Approach.** Read the order's status before calling LedgerOne.

**Main steps.** In `OrderProcessor.ProcessAsync`, after the upsert, retrieve the order's status; if it's already *Approved*, return without calling LedgerOne. Keep the LedgerOne idempotency key as a second line of defence.

**Check.** A test substitutes Dataverse to return an *Approved* order and asserts that the LedgerOne client receives no call.

**Watch out for.** The upsert at the start of processing sets the status to *Received*. Read the existing status **before** that upsert, or change the upsert so it doesn't overwrite a later status.

## Chapter 25 – Production Readiness and Final Architecture

**Approach.** Be honest rather than complete: a checklist with real gaps and owners is worth more than one with every box ticked.

**Main steps.** Answer each line of the checklist for a system you work on, using Lab 25's commands where they apply. Rank the gaps by impact and likelihood.

**Check.** Each of your three biggest gaps has an owner, a date and a first step that can start this week.
