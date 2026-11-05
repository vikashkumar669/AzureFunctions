---
chapter: 24
part: "Part V — Capstone Projects"
title: "Project 4 — Event-Driven Order Processing"
build-summary: "Maintenance partners submit service orders through the Partner API; each is accepted in milliseconds, recorded in Dynamics 365 and LedgerOne exactly once, retried through outages, and, when it can't be processed, parked with a reason and replayed after the fix."
builds:
  - "An intake endpoint that validates, de-duplicates and queues orders"
  - "A processor that separates transient from permanent failures"
  - "Dead-letter triage and a replay tool"
versions: ".NET 10 · Service Bus extension 5.24.0 · Azure.Messaging.ServiceBus · Microsoft.Extensions.Http.Resilience"
---

::: objectives
- Split a request into fast acceptance and asynchronous processing.
- Use the order number end to end for duplicate detection and idempotency.
- Classify failures as transient or permanent and settle messages accordingly.
- Triage dead-lettered messages and replay them safely after a fix.
- Give partners a way to check an order's status.
:::

## The business problem

When a boiler fails, a maintenance partner raises a service order. Each order must reach Dynamics 365, so agents see it on the property, and LedgerOne, so it can be paid. Partners send orders in bursts after site visits, sometimes twice (their systems retry too), and occasionally with mistakes such as a property code that doesn't exist.

Requirements: answer partners immediately; never lose an order; never record one twice; don't let one bad order block the others; and let an operator fix and resend failures.

## How it works

The design combines patterns from earlier chapters (@fig:orders):

1. **Accept fast.** `SubmitOrder` validates the request and puts it on the `order-intake` queue. It answers **202 Accepted** with a status URL, well within any HTTP limit, before any slow system is involved.
2. **One key everywhere.** The partner's order number is the Service Bus `MessageId` (duplicate detection discards a resend within the window), the Dataverse alternate key (upsert, not create), and LedgerOne's idempotency key. A duplicate at any stage has no effect.
3. **Process with classification.** `ProcessOrder` upserts the order in Dataverse, submits it to LedgerOne and marks it approved. A **permanent** failure (invalid data, unknown property, a 400 from LedgerOne) is dead-lettered at once with a reason. A **transient** one (timeouts, 429, 5xx, throttling) is abandoned and retried by Service Bus, up to the max delivery count.
4. **Triage and replay.** Dead-lettered orders keep their reason. An operator fixes the cause (creates the missing property, corrects mapping) and replays them with the Ops tool.

![Orders are accepted in milliseconds, processed from a queue, retried when the failure is temporary, and parked with a reason when it isn't.](figures/ch24/orders.svg){#fig:orders}

## Architecture

::: {#tbl:components caption="Order pipeline components"}
| Component | Role | Authenticates with | Failure path |
|---|---|---|---|
| `SubmitOrder` | Validate, queue, 202 | APIM → Easy Auth (Chapter 14) | 400 with field errors |
| `order-intake` queue | Buffer, duplicate detection (1 h) | Managed identity (send/receive) | DLQ after 10 deliveries |
| `ProcessOrder` | Upsert, submit, settle | Managed identity; LedgerOne key | Abandon or dead-letter |
| `GetOrderStatus` | Status for partners | APIM | 404 until processed |
| `Harbourline.Ops dlq-peek/replay` | Triage, resend | Operator's sign-in | — |
:::

## Prerequisites

Chapters 8, 11, 14 and 18. The `order-intake` queue with duplicate detection is created by Bicep (Chapter 20).

## Step-by-step implementation

### Step 1 – The contract

```{.csharp #lst:ordercontract include="src/Harbourline.Connect.Contracts/Orders/OrderSubmission.cs" region="order-contract" caption="What partners send and receive"}
```

### Step 2 – Accept and queue

```{.csharp #lst:accept include="src/Harbourline.Connect.Functions/Orders/OrderFunctions.cs" region="order-accept" caption="SubmitOrder: validate, send with the order number as message ID, answer 202"}
```

1. Validation runs before queueing, so partners get field errors immediately and bad orders never enter the pipeline.
2. The order number becomes the `MessageId`; with duplicate detection on the queue, a partner's retry within an hour is discarded by Service Bus.
3. 202 with a `Location`-style status URL tells the partner where to look.

The sender is a singleton `ServiceBusSender` created from a `ServiceBusClient` that authenticates with the app's identity (registered in `HostingExtensions.cs`).

### Step 3 – Process idempotently

```{.csharp #lst:process include="src/Harbourline.Connect.Core/Orders/OrderProcessor.cs" region="order-process" caption="Upsert in Dataverse, submit to LedgerOne, mark approved"}
```

1. Invalid data is permanent: retrying won't change it.
2. Upsert by order number: a redelivered message updates the same row.
3. An unknown property code is reported by Dataverse as "not found" for the lookup's key, and is permanent until someone creates the property.
4. LedgerOne receives the order number as its idempotency key (Chapter 8), so a retry after a lost response doesn't create a second order.

### Step 4 – Settle by failure type

```{.csharp #lst:processfn include="src/Harbourline.Connect.Functions/Orders/OrderFunctions.cs" region="order-process-fn" caption="ProcessOrder: a span per message, then complete, dead-letter or abandon"}
```

1. Each message gets a span tagged with the order number and delivery count, so one KQL query shows an order's whole history (Chapter 18).
2. Permanent failures, including unreadable JSON, are dead-lettered with a reason (`Permanent` or the exception type) and a description, and counted in the dead-letter metric.
3. Transient failures are abandoned and rethrown, so Service Bus redelivers the message and monitoring records the failed attempt. After the tenth delivery, Service Bus dead-letters the message itself.

The classification rules are `FailureClassifier` from Chapter 11, tested case by case in `OrderTests`.

### Step 5 – Answer status queries

`GET /api/v1/orders/{number}` reads the order's status from Dataverse: `Received`, `Approved` or `Rejected`, or 404 while it's still in the queue. Partners poll it, or Harbourline can add a callback later.

### Step 6 – Triage the dead-letter queue

The dead-letter alert (Chapter 18) fires. The operator lists the messages:

```bash
dotnet run --project samples/Harbourline.Ops -- \
  dlq-peek sb-hlconnect-prod-….servicebus.windows.net order-intake
```

Each line shows the order number, the reason, when it was enqueued and the description. Typical causes and actions:

::: {#tbl:triage caption="Dead-letter triage guide"}
| Reason / description | Cause | Action |
|---|---|---|
| `Permanent`: property … does not exist | Property not yet in Dynamics 365 | Create it, then replay |
| `Permanent`: category must be one of … | Partner sent an unsupported value | Ask the partner to resend corrected; don't replay |
| `HttpRequestException` … 400 | LedgerOne rejected the data | Fix mapping or data, then replay |
| `MaxDeliveryCountExceeded` | Transient failures for a long time (outage) | Confirm recovery, then replay all |
:::

### Step 7 – Replay

```{.csharp #lst:replay include="samples/Harbourline.Ops/DeadLetterTools.cs" region="dlq-replay" caption="Replaying dead-lettered messages, optionally filtered by reason"}
```

1. The receiver reads the queue's dead-letter sub-queue.
2. Messages with other reasons are released untouched.
3. A copy keeps the body, message ID and properties, and is marked as a replay.
4. The dead letter is completed only after the copy is sent, so a crash can't lose an order; at worst it is replayed twice, which the order number makes harmless.

Note that duplicate detection may discard a replay whose original was accepted within the last hour. For replays inside the window, wait, or shorten the window deliberately for the replay.

## Screens you will use

::: {.capture #fig:dlq-peek caption="Triage: dead-lettered orders with their reasons." app="Terminal" path="dotnet run --project samples/Harbourline.Ops -- dlq-peek …" annotations="1 Order number; 2 Reason; 3 Description"}
:::

::: {.capture #fig:order-trace caption="One order's history across attempts in Application Insights." app="Azure portal" path="Application Insights › Transaction search › ProcessOrder (order.number = ORD-1001)" annotations="1 First attempt: LedgerOne 503; 2 Second attempt: success; 3 delivery.count tag"}
:::

## Complete working code

`src/Harbourline.Connect.Contracts/Orders/`, `src/Harbourline.Connect.Core/Orders/`, `src/Harbourline.Connect.Functions/Orders/OrderFunctions.cs`, `samples/Harbourline.Ops/DeadLetterTools.cs`, `tests/.../Orders/OrderTests.cs`.

## Configuration

Queue settings in Bicep: `maxDeliveryCount: 10`, `lockDuration: PT1M`, `requiresDuplicateDetection: true`, `duplicateDetectionHistoryTimeWindow: PT1H`. App settings: `ServiceBus__fullyQualifiedNamespace` and the LedgerOne settings from Chapter 8.

## Sample input and output

```text
POST /partner/v1/orders
{"orderNumber":"ORD-1001","propertyCode":"HBR-0042",
 "category":"Heating","amount":180.00,"partnerId":"PARTNER-7"}

HTTP/1.1 202 Accepted
Location: /api/v1/orders/ORD-1001
{"orderNumber":"ORD-1001","statusUrl":"/api/v1/orders/ORD-1001"}
```

## Testing

`OrderTests` covers the classifier for every failure type, rejection of invalid orders before any call, an unknown property becoming permanent without calling LedgerOne, and the order of operations for a valid order (Dataverse before LedgerOne). End to end, against the mocks with `FailureRate=0.5`: submit 100 orders, then check that Dataverse and the mock both hold exactly 100, and that the logs show retries.

## Common errors and troubleshooting

::: {#tbl:errors caption="Errors you may see with order processing"}
| Symptom | Likely cause | Fix |
|---|---|---|
| Partner retries create duplicates | Different message IDs per retry | Use the order number as `MessageId` |
| Replayed messages vanish | Duplicate detection discarded them within the window | Replay after the window, or adjust it for the replay |
| Valid orders dead-lettered | A transient failure classified as permanent | Fix `FailureClassifier`; replay |
| DLQ grows during an outage | Expected: deliveries exhausted | Replay after recovery; consider a longer retry horizon |
:::

## Security considerations

::: security
Partners can only queue orders through APIM, after validation. The queue accepts messages only from the app's identity. The replay tool runs with an operator's own sign-in and *Azure Service Bus Data Owner* on that queue, granted just in time, not permanently.
:::

## Performance and scalability

::: performance
Acceptance is one validation and one send: fast and independent of Dataverse and LedgerOne. Processing scales with the queue, limited by `maxConcurrentCalls` and maximum instances, which you size against Dataverse's limits and LedgerOne's capacity (Chapters 2, 11, 15).
:::

## Alternatives and design decisions

Processing synchronously in the HTTP request would be simpler and would fail exactly when partners send most: during outages and bursts. A Durable Functions orchestration per order would add visibility for multi-step processes; for two steps, a queue and idempotent writes are enough.

## Best practices

- Accept fast, process asynchronously, return 202 with a status URL.
- Use one business key as message ID, alternate key and idempotency key.
- Dead-letter permanent failures at once with a clear reason; retry transient ones.
- Alert on the dead-letter queue and keep a tested replay tool.
- Make replays safe by design, not by care.

## Hands-on exercise

::: exercise
**Goal:** reject duplicates that arrive outside the duplicate-detection window: make `ProcessOrder` skip the LedgerOne call when the order is already `Approved` in Dataverse, and prove it with a test.
:::

## Knowledge check

::: knowledge-check
1. In how many places is the order number used, and what does each use protect against?
2. Why does `SubmitOrder` validate before queueing?
3. What happens to an order whose property doesn't exist yet, and how is it recovered?
4. Why is the dead letter completed only after the replayed copy is sent?
:::

Answers are in Appendix G.

::: summary
- Fast acceptance plus queued processing keeps partners responsive and orders safe.
- One business key makes every stage idempotent.
- Permanent failures go straight to the dead-letter queue with a reason; transient ones retry.
- Triage and replay close the loop; replays are safe because every write is idempotent.
:::

## References

- [Asynchronous Request-Reply pattern](https://learn.microsoft.com/azure/architecture/patterns/async-request-reply) – Microsoft Learn, checked 2026-10-27.
- [Overview of Service Bus dead-letter queues](https://learn.microsoft.com/azure/service-bus-messaging/service-bus-dead-letter-queues) – Microsoft Learn, checked 2026-10-27.
- [Duplicate detection](https://learn.microsoft.com/azure/service-bus-messaging/duplicate-detection) – Microsoft Learn, checked 2026-10-27.
- [Queue-Based Load Leveling pattern](https://learn.microsoft.com/azure/architecture/patterns/queue-based-load-leveling) – Microsoft Learn, checked 2026-10-27.

<!-- Glossary terms introduced in this chapter: asynchronous request-reply, permanent failure, transient failure, triage, replay -->
