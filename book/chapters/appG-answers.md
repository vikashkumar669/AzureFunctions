---
chapter: "G"
appendix: true
title: "Answers to the Knowledge Checks"
---

Short answers to the questions at the end of each chapter. Where an answer depends on a Microsoft limit or behaviour, the chapter's references give the source.

## Chapter 1

1. **In a synchronous plug-in** registered on Create and Update of `account` (PreValidation or PreOperation). Only code inside the Dataverse transaction, or a synchronous webhook, can stop the save; an asynchronous function sees the record after it is committed.
2. Dataverse throws a `TimeoutException` and rolls back the entire message operation.
3. Any two of: webhooks support synchronous and asynchronous steps, Service Bus only asynchronous; Service Bus queues messages and absorbs high volume, while webhooks scale only as far as the receiving service can; webhooks authenticate with a header or query-string key, Service Bus with a shared access signature; a webhook waits at most 60 seconds for a response.
4. Memory of instances while they actively execute functions (gigabyte-seconds) and the number of executions, each after a monthly free grant.
5. Because the platform scales out by running many instances at once, and events can be delivered more than once. Code that assumes it runs alone, or that an event arrives exactly once, creates duplicates or overwrites data.

## Chapter 2

1. Exactly one trigger. Bindings are optional: zero, one or several input and output bindings.
2. Your code runs in the isolated worker process (your .NET 10 app, started in `Program.cs`). The Functions host, a separate process run by Azure, listens to triggers and runs bindings.
3. `Health`, `GetProperties` and `RequestLeaseSignature` share the `http` group; `OnPropertyFileArrived` is in the `blob` group; Durable functions would share the `durable` group. `OnOrderSubmitted` (Service Bus) and `NightlyInvoiceSync` (timer) each scale on their own instances.
4. 10 × 16 = 160 concurrent invocations, each potentially calling Dataverse. That is about three times Dataverse's default service protection limit of 52 concurrent requests per user per web server, so many calls would receive HTTP 429 and have to wait for `Retry-After`.
5. Every HTTP-triggered function must respond within 230 seconds because of the Azure load balancer's idle timeout. Return 202 Accepted and do the work asynchronously, for example with a queue or a Durable Functions orchestration.

## Chapter 3

1. Azure Storage (Blob, Queue, Table). The host itself uses storage for locks, timer schedules and internal state, whatever triggers your functions use.
2. `DefaultAzureCredential` tries credential sources in order: the managed identity in Azure, developer sign-ins such as the Azure CLI on a laptop.
3. Functions may reference Core and Contracts; Core may reference Contracts; Contracts references nothing. Core stays free of Functions types so its logic is testable and reusable outside the host.
4. The same .NET SDK (and Functions build SDK and test runner) on every machine and in CI. Without it, a newer SDK can change compiler behaviour or warnings, and builds differ between machines.
5. Shared environments drift and developers' test data collide; a personal environment is free, isolated and quickly rebuilt by the setup tool.

## Chapter 4

1. Any three: the same tables and data, the same security roles, the same plug-ins and business logic, the same service protection limits.
2. The publisher's option value prefix is 10,000, so choice values start at 10,000 × 10,000 = 100,000,000.
3. A column or set of columns with unique values that identifies a row instead of its GUID. It makes upsert (and lookups by business key) possible.
4. Send `Prefer: odata.include-annotations="OData.Community.Display.V1.FormattedValue"` and read `<column>@OData.Community.Display.V1.FormattedValue`.
5. Rows inserted or deleted between requests shift pages, so rows are missed or repeated. Pass the paging cookie (SDK) or follow `@odata.nextLink` (Web API).

## Chapter 5

1. The token proves the caller's identity to Dataverse for a limited time. The application user's security roles decide what it may do.
2. There is no credential to store, leak, expire or rotate; Azure manages it for the resource.
3. It tries sources in order: in Azure the managed identity answers; on a laptop the managed identity isn't available, so it uses the developer's Azure CLI or Visual Studio sign-in.
4. The object (principal) ID, used for Azure role assignments, and the application (client) ID, used to create the Dataverse application user.
5. Add the Write privilege on the Lease table, at the needed depth, to the integration's security role.

## Chapter 6

1. Options are validated when the host starts instead of on first use, so a missing or invalid setting stops the deployment visibly at start-up rather than failing the first request.
2. Creating one is expensive (authentication, metadata), and its async methods are safe to call concurrently, so one instance serves all invocations.
3. It retrieves the row by alternate key: an `EntityReference` built from the table, the `hl_propertycode` key and the code.
4. A missing property is a normal business outcome (404), not a failure; only real faults should surface as exceptions.
5. A function key. Chapter 14 replaces it with Microsoft Entra ID tokens through API Management.

## Chapter 7

1. Clients change one or two fields at a time; nullable fields mean "not sent", so a partial update can't overwrite fields the client didn't intend to change.
2. The ETag from its last read, in `If-Match`. If another save changed the row version, Dataverse rejects the update and the API returns 412 Precondition Failed.
3. A unique final sort key makes the order deterministic when names are equal, so pages neither repeat nor skip rows.
4. The next page number, the paging cookie and a hash of the filters. The hash stops a token from one query being used for another.
5. Breaking changes: removing or renaming fields, changing types, meanings or status codes. Adding optional fields or parameters doesn't need a new version.

## Chapter 8

1. Socket exhaustion from creating clients per call, and stale DNS from one static client; it also centralises configuration.
2. Total timeout, retry, circuit breaker, attempt timeout (preceded by a rate limiter in the standard pipeline).
3. If the first attempt succeeded but its response was lost, a retry repeats the side effect. An idempotency key or an idempotent method (such as `PUT` to a named resource) makes it safe.
4. Each retry would look like a new operation to the server, so duplicates aren't detected.
5. With a Microsoft Entra ID token for the Function App's managed identity, added by `BearerTokenHandler`. No secret is stored for calling it; only the callback secret for verifying SignBridge's callbacks.

## Chapter 9

1. The JSON RemoteExecutionContext. Changed columns are in `InputParameters` under `Target`; other values come from registered entity images.
2. The request leaves Dataverse before the transaction commits; if the operation later rolls back, the function may already have acted on a change that never happened.
3. 502, 503 and 504; Dataverse makes one more attempt.
4. Defence in depth: the key proves knowledge of a secret, the organization ID proves the event came from the expected environment, so a copied key in another environment can't trigger production work.
5. For high or bursty volume, when you want buffering on the Dataverse side, or several consumers of the same events.

## Chapter 10

1. 06:30:00 on weekdays (Monday to Friday), in UTC on Flex Consumption.
2. Changes that arrive while the job is running are then included in the next run instead of being missed.
3. Every write is an upsert by alternate key, so rewriting the same row gives the same result.
4. A new run doesn't start while the previous one is still running; the timer is a singleton across instances.
5. So the invocation is recorded as failed and monitoring can alert; the failed rows are retried next run because the watermark didn't advance.

## Chapter 11

1. A queue delivers each message to one receiver; a topic delivers a copy to every subscription, each behaving like its own queue.
2. Service Bus makes it available again and increments its delivery count; after the max delivery count it is dead-lettered automatically.
3. Delivery is at least once: a crash after the work but before completion redelivers the message.
4. Permanent failures (invalid data, 4xx client errors, missing references), because retrying can't succeed and only delays other messages.
5. Only send to the `account-events` topic. If its key leaked, it couldn't read messages or touch other entities.

## Chapter 12

1. The orchestrator runs again from the start, replaying recorded results from its history, until it reaches the next new step.
2. Any three: no `DateTime.UtcNow` (use `context.CurrentUtcDateTime`); no I/O or HTTP calls (use activities); no `Task.Delay` (use durable timers); no new GUIDs or random numbers (generate them in activities); no reading changeable configuration.
3. `Task.WhenAny` of `WaitForExternalEvent` and a durable timer set three days ahead; the loser timer is cancelled.
4. So there is at most one workflow per lease: a repeated start finds the running instance, and the callback can find it by lease number.
5. SignBridge can't hold a function key. An HMAC-SHA256 signature over the body, verified in constant time with a shared secret from Key Vault, protects it.

## Chapter 13

1. Only the Event Grid source. You must create an event subscription for BlobCreated events, sending them to the blobs extension webhook with the `blobs_extension` key.
2. `UpsertMultiple` rejects a request that contains the same key twice, and a duplicate in a feed is usually a partner error worth reporting.
3. They read one record at a time from a stream and yield it; the importer buffers at most one batch.
4. Reports (line, code, reason) for every imported file go to `feed-reports`; files that can't be read at all go to `feed-quarantine`.
5. Rows are upserted by property code, so a second import updates the same rows to the same values.

## Chapter 14

1. Per-partner identity and keys, token validation with roles, rate limits and quotas, central policy, safe error handling, and a developer portal.
2. It is issued for the Partner API's audience and carries the `Orders.Submit` app role in its `roles` claim, which APIM's `validate-azure-ad-token` policy requires.
3. `rate-limit-by-key` isn't available on the Consumption tier; with one subscription per partner, `rate-limit` throttles each partner separately.
4. App Service authentication on the Function App requires a token and accepts only API Management's managed identity as client.
5. Defence in depth: a gateway misconfiguration must not open the data, and the function's validation is the definitive business rule.

## Chapter 15

1. Number of requests (6,000), combined execution time (20 minutes) and concurrent requests (52), each per user, per web server, over a sliding five-minute window (default values).
2. The request limit caps one-row requests at 6,000 per five minutes, about 20 rows per second, whatever the parallelism.
3. The whole request fails and none of its rows are written: it is a transaction.
4. `ServiceClient` has already waited and retried them; if throttling still surfaces, the load exceeds Dataverse's capacity and splitting would only add requests.
5. Throughput depends on the environment, table, plug-ins, data and time; a number from another environment would mislead. The book gives a method instead.

## Chapter 16

1. Extract, transform (map and validate), load, reconcile.
2. The business confirmed they are demolished properties that must not be migrated; a default would publish them as available.
3. Checkpoints skip completed batches on restart; upsert makes repeating the interrupted batch harmless.
4. The migrated fields of one row joined in a fixed order with a separator. Two decimals make equal amounts compare equal however they were written.
5. Rows created in Dataverse after go-live would otherwise make the comparison fail although the migrated set is correct.

## Chapter 17

1. EPPlus requires a commercial licence for commercial use; Office interop/automation is not supported on servers.
2. For very large, plain extracts where holding the workbook in memory is too costly.
3. Anyone can remove it; it guides users but the import must validate everything anyway.
4. The request was valid and processed; individual rows failed, which the body reports. The HTTP status describes the request, not each row.
5. Labels people read (status) use formatted values; numbers and dates are written as typed raw values with number formats.

## Chapter 18

1. The Functions host (`"telemetryMode": "OpenTelemetry"` in host.json) and the worker (`AddOpenTelemetry().UseFunctionsWorkerDefaults()` plus an exporter).
2. The trace ID, shown as the operation ID.
3. Properties stay structured and queryable, and source-generated methods avoid work when the level is disabled.
4. Anyone who has the connection string sending telemetry: only identities with the right role can write.
5. Function failures, dead-lettered messages, Dataverse throttling, and missing scheduled runs.

## Chapter 19

1. Core has no Functions types; dependencies are interfaces (including `TimeProvider`); functions are thin.
2. That the service builds the intended requests and handles every answer correctly. It can't prove that Dataverse accepts the request.
3. Configure it with `.ThrowsAsync(Faults.Of(DataverseErrors.ConcurrencyVersionMismatch))`.
4. They couple tests to implementation details and break on harmless refactoring; use them only when order is a requirement.
5. Warnings such as possible null dereferences being ignored and accumulating into runtime bugs.

## Chapter 20

1. All access to the storage account must use Microsoft Entra ID identities, not account keys. The app uses identity-based settings and roles for host storage, deployment and triggers.
2. Redeploy the previous build's artifact through the pipeline, which starts another rolling update.
3. Tokens from GitHub's OIDC issuer whose subject matches the repository and environment. GitHub stores only client, tenant and subscription IDs.
4. So exactly what was tested in dev and test is what reaches prod.
5. Dataverse solutions first, then Azure code, so code never runs against schema it expects but doesn't find; changes are kept backward compatible.

## Chapter 21

1. A change caused by the sync's own write. Its synced fields hash to the value stored at the last sync, so it is skipped.
2. It compares each side's hash with the last synced hash: if only one differs, that side changed; if both differ, it's a conflict.
3. So the two directions rarely process the same customer at the same moment.
4. A retry of the same change reuses the key and is recognised; a later, different change has a different hash and is processed.

## Chapter 22

1. From the `X-MS-CLIENT-PRINCIPAL` header set by App Service authentication, which validates the token and strips any copy sent by callers.
2. The intersection of the impersonator's privileges and the impersonated user's privileges.
3. The shared singleton is used by all invocations; setting the caller on it would affect other requests. The clone is set per request and disposed.
4. Answering 403 would confirm that the lease exists; 404 reveals nothing.

## Chapter 23

1. The query seeks directly to the position after the last row using the sort columns, instead of skipping all earlier rows.
2. It shows whether another page exists without a separate count query.
3. Responses that depend only on the request's parameters and are the same for every caller; never responses that vary by identity or permissions.
4. A written question, targets and pass criteria, a production-like environment, a repeatable script and recorded settings and versions.

## Chapter 24

1. Three: the Service Bus `MessageId` (duplicate detection against partner resends), the Dataverse alternate key (upsert, no duplicate rows) and LedgerOne's idempotency key (no duplicate ledger orders on retry).
2. Partners get field errors immediately, and invalid orders never enter the pipeline.
3. It is dead-lettered as permanent with a reason; after the property is created, the operator replays it.
4. So a crash between the two can't lose the order; at worst it's replayed twice, which the order number makes harmless.

## Chapter 25

1. Reliability, security, cost optimization, operational excellence and performance efficiency.
2. Recovery time objective: how long a capability may be unavailable. Recovery point objective: how much data may be lost. The business sets both.
3. Redeploy the Bicep templates and the last artifact into another region and reconfigure endpoints; state lives in Dataverse and Service Bus.
4. For example: Flex Consumption execution (cap maximum instances, choose instance size), telemetry ingestion (log levels, sampling, retention), APIM calls (caching, rate limits).
