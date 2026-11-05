---
chapter: "H"
appendix: true
title: "Interview Questions"
---

Questions for interviewing, or being interviewed for, a Dynamics 365 integration role. Each points to the chapter where the answer is developed. Good answers explain trade-offs, not just definitions.

## Foundations

1. When would you implement a rule as a plug-in, an Azure Function or a Power Automate flow? (1)
2. What runs where in the isolated worker model, and why does it matter for dependencies? (2)
3. How does Flex Consumption scale, and how could scaling hurt Dataverse? (2, 15)
4. What does the 230-second HTTP limit mean for API design? (2, 24)

## Dataverse

5. When would you use the Web API rather than the SDK for .NET? (4)
6. Why are alternate keys important in integrations? (4, 10, 16)
7. How do you page through 200,000 rows safely? (4, 7, 23)
8. Explain optimistic concurrency in Dataverse and how you'd expose it in a REST API. (7)
9. What are the service protection limits, and how do you design within them? (15)
10. On a standard table, what happens when one row in a bulk request fails, and how do you handle it? (15)
11. How does change tracking work, and what happens if a token expires? (10)

## Security

12. Walk through how a function authenticates to Dataverse without a secret. (5)
13. What privileges would you give an integration's application user, and how do you decide? (5)
14. How would you secure a webhook endpoint that Dataverse calls? (9)
15. How do you make sure a user can only export records they can see in Dynamics 365? (22)
16. How do you remove secrets from a CI/CD pipeline? (20)

## Integration patterns

17. What makes an operation idempotent, and how do you make a POST safe to retry? (8)
18. Webhook or Service Bus endpoint for Dataverse events: how do you choose? (9, 11)
19. How do you decide whether a failed message is retried or dead-lettered? (11, 24)
20. Design a nightly incremental load. Where do you store the watermark, and when do you advance it? (10)
21. How would you prevent loops in a two-way synchronization? (21)
22. When are Durable Functions the right tool, and what constraints do they impose? (12)
23. How would you import a partner's file with bad rows in it? (13)
24. How would you plan and prove a data migration? (16)

## Operations

25. How do you trace one business transaction across functions, queues and Dataverse? (18)
26. Which alerts would you set up first for an integration app? (18, 25)
27. How do you deploy to Flex Consumption with no downtime, and how do you roll back? (20)
28. How would you measure bulk-load throughput, and why not trust published numbers? (15)
29. What goes in a production-readiness review? (25)
30. Tell me about an integration failure you investigated: how did you find the cause? (18)

## Model answers

The answers below are what a strong candidate might say in two or three minutes. They're starting points for discussion, not scripts: an interviewer learns more from how a candidate reasons about trade-offs than from whether they use the same words.

**1. Plug-in, function or flow?** A plug-in when the logic must run inside the Dataverse transaction: validate and block a save, or change data atomically with it. A flow when the logic is simple orchestration that business users should own and change, with modest volume. A function when it needs engineering: guaranteed delivery, volume, streaming files, a public API contract, long-running state, or libraries. Mention ownership and the cost of change, not only technical capability.

**2. Isolated worker.** The Functions host and your code run in separate processes. The host handles triggers, scaling and bindings; the worker runs your .NET code with its own dependencies and .NET version. That means your package versions don't conflict with the host's, you can use standard .NET dependency injection and middleware, and you choose the .NET release you target. A strong answer mentions that HTTP requests are forwarded from the host to the worker, which is why ASP.NET Core integration exists.

**3. Flex Consumption and Dataverse.** Flex scales per function group: HTTP functions together, Event Grid blob triggers together, Durable triggers together, and every other function on its own. Each instance handles a configured concurrency. Concurrent Dataverse calls are roughly instances × concurrency × calls per execution, all from one application user, so a burst can hit service protection limits. Control it with the maximum instance count, per-instance concurrency and batching.

**4. The 230-second limit.** An HTTP request to a function must be answered within about 230 seconds, whatever the function timeout, because of the platform's front-end idle timeout. Long work therefore can't be done synchronously in an HTTP call. The pattern is: accept the request, queue the work, return 202 Accepted with a status URL, and process in the background (Chapter 24), or use Durable Functions with its status endpoints.

**5. Web API or SDK?** The SDK for .NET (`ServiceClient`) is the natural choice in C#: typed requests, built-in throttling retries, bulk messages and early-bound support. The Web API suits other languages, lightweight calls, and cases where you want plain HTTP control. Both reach the same platform features. A strong answer adds that `ServiceClient` should be created once and reused.

**6. Alternate keys.** They let you identify rows by a business key that both systems share (property code, invoice number) instead of a GUID only Dataverse knows. That enables upserts, which make integrations idempotent: a retried write updates instead of duplicating. Keys are unique indexes, so they also prevent duplicates under concurrency and make lookups fast. They must be **active** before you rely on them.

**7. Paging 200,000 rows.** Use the server's paging (paging cookie with the SDK, `@odata.nextLink` with the Web API) with an explicit sort, and select only needed columns. For resumable jobs use keyset paging on an indexed key and store the last key processed. Never fetch everything to count it, and never build page links yourself.

**8. Optimistic concurrency.** Every row has a version number. Return it as an ETag; require clients to send it back in `If-Match`; send the update with `ConcurrencyBehavior.IfRowVersionMatches`. If someone else changed the row, Dataverse refuses with a version-mismatch error, which the API returns as 412 Precondition Failed. No locks are held while a user edits.

**9. Service protection limits.** Per user, per web server, over a five-minute sliding window: number of requests, combined execution time, and concurrent requests. Design within them by batching (bulk messages), limiting parallelism (start from `RecommendedDegreesOfParallelism`), capping scale-out, honouring `Retry-After`, and measuring with production-like data. A strong answer notes that one application user means one budget for the whole integration.

**10. A failing row in a bulk request.** On standard tables, bulk messages run in a transaction, so one bad row fails the whole batch. Handle it by splitting the batch and retrying halves until the bad row is isolated, then recording it as a rejection while the rest are written (Chapter 15). On elastic tables, partial success is possible and the response reports per-row failures.

**11. Change tracking.** Enable it on the table; the first request returns all rows and a data token; each later request with the token returns new or updated rows and deleted IDs since then. Store the token after processing. If the token has expired, start again with a full read and a fresh token, and make sure downstream writes are idempotent so the repeat is harmless.

**12. Secret-free authentication.** The Function App has a managed identity. The code asks `DefaultAzureCredential` (or `ManagedIdentityCredential`) for a token for the environment's `/.default` scope. In Dataverse, an application user is created for that identity's client ID and given a security role. No secret exists anywhere; locally, the same code uses the developer's own sign-in.

**13. Application user privileges.** Least privilege: a custom role with only the tables and operations the integration needs, at the scope it needs, and no delete unless required. Derive it from the integration's operations, test it in a non-production environment, and never use System Administrator. Separate powerful privileges (impersonation) into their own roles assigned only where needed.

**14. Securing a webhook.** Use HTTPS, a function key (or another authentication option the registration supports), and verify that the body's organization ID matches the expected environment. Keep the function fast, return 2xx for events you ignore, and consider network restrictions. Don't trust request headers for security decisions.

**15. Exports limited to what users can see.** Let Dataverse enforce it: run the queries as the user, through impersonation with the user's object ID from a trusted source (Easy Auth), or through the on-behalf-of flow. Don't re-implement security rules in the function. Check that Easy Auth is enforced, because the user identity header is only trustworthy when it is.

**16. Secrets out of CI/CD.** Use OIDC federation: the pipeline gets a short-lived token from its platform, Entra ID trusts it for a specific repository and environment through a federated credential, and the deployment identity has roles scoped to its resource group. No client secrets to store, rotate or leak.

**17. Idempotency.** An operation is idempotent if doing it twice has the same effect as once. Upserts by business key, setting absolute values, and checking state before acting are idempotent. To make a POST safe to retry, give it an idempotency key that identifies the business operation (an order number, not a random GUID) and have the receiver return the original result for a repeated key.

**18. Webhook or Service Bus?** Webhooks are simple and fast but lose events if the receiver is down beyond one retry. The Service Bus endpoint stores messages until they're processed, supports back-pressure and dead-lettering, and is asynchronous only. Choose Service Bus when events must not be lost or volume is high; a webhook for simple, low-volume, or synchronous validation cases.

**19. Retry or dead-letter?** Classify failures. Transient ones (timeouts, 429, 5xx, throttling, broken circuits) are abandoned and retried, up to a maximum delivery count. Permanent ones (validation failures, missing referenced data, 4xx other than 408 and 429) are dead-lettered immediately with a reason. Every dead-lettered message has an owner and a replay path.

**20. A nightly incremental load.** Store the watermark as business state (Dataverse, so it's visible and backed up). Read changes since the watermark minus a small overlap, upsert by key, and advance the watermark only after every change has been processed. A failed run leaves the watermark where it was, so the next run repeats the window.

**21. Loops in two-way sync.** Store a hash of the synchronized fields on the record after each sync. A change whose fields hash to the stored value is an echo of your own write and is skipped. The same hash reveals conflicts: if both sides differ from it, both changed, and field-ownership rules merge them.

**22. Durable Functions.** Right for workflows that wait (for people, timers or external events), fan out and in, or need durable state across steps. Constraints: orchestrators must be deterministic (no I/O, no current time or random values except through the context), histories grow with each step, and changing an orchestrator while instances run needs versioning.

**23. A partner file with bad rows.** Stream it, validate row by row with the same rules as the API, import valid rows idempotently, and write a report listing each rejected row with its line number and reason. Quarantine unreadable files. Never drop rows silently, and make repeated imports harmless.

**24. Planning and proving a migration.** Write the mapping specification with the business, implement it as tested code, load in checkpointed batches, and reconcile counts and checksums against the source. Rehearse on production-like data until timings and rejections are known, agree go/no-go criteria and a rollback plan, and switch on integrations only after the go decision.

**25. Tracing one transaction.** Use OpenTelemetry with W3C trace context: HTTP calls and Service Bus messages carry it, so one operation ID links the request, the queue processing and the dependencies. Add business keys (order number) as span tags and log properties, and log Dataverse's correlation ID to link to platform-side traces.

**26. First alerts.** Dead-letter depth above zero for a period; scheduled jobs that haven't succeeded within their window; sustained failure rate per function; sustained Dataverse throttling. Each alert actionable, routed to people who can act, and linked to a runbook. Everything else on a dashboard.

**27. Deploying to Flex Consumption.** Build once, deploy the same artifact to each environment through a pipeline with OIDC. Use the rolling update strategy where available so instances are replaced gradually. There are no slots; rollback is redeploying the previous artifact, so keep artifacts and make schema changes additive.

**28. Measuring throughput.** Define the question and the success criteria, generate representative data, vary one setting at a time (batch size, then parallelism), repeat runs and use medians, and watch for throttling. Published numbers describe someone else's environment: tables, plug-ins, network and limits differ.

**29. Production-readiness review.** Security (identities, secrets, network, least privilege), reliability (retries, idempotency, dead-letter handling, recovery objectives), operations (telemetry, alerts, runbooks), performance (measured against targets), cost (drivers, budgets), and delivery (pipelines, rollback). Each item has evidence and an owner, and the review is repeated when the architecture changes.

**30. An investigation story.** Look for structure: the symptom and how it was detected, how the candidate narrowed it down (telemetry, reproduction, hypotheses tested one at a time), the root cause, the fix, and what changed afterwards so it couldn't happen silently again (a test, an alert, a runbook entry). Blame-free language and a clear timeline are good signs.
