---
chapter: 11
part: "Part III — Real-World Integration Patterns"
title: "Azure Service Bus and Event-Driven Architecture"
build-summary: "Account changes in Dynamics 365 flow to LedgerOne through a Service Bus topic: buffered when LedgerOne is slow, retried when it fails, and parked in a dead-letter queue, never lost, when a message can't be processed."
builds:
  - "A Dataverse service endpoint that posts account changes to a topic"
  - "A Service Bus–triggered function that settles every message explicitly"
  - "Dead-lettering rules that separate transient from permanent failures"
versions: ".NET 10 · Service Bus extension 5.24.0 · Azure.Messaging.ServiceBus (via the extension) · Standard tier"
---

::: objectives
- Choose between queues and topics, and explain subscriptions, sessions and duplicate detection.
- Register a Dataverse service endpoint that posts to Service Bus in JSON format.
- Connect a function to Service Bus with a managed identity instead of a connection string.
- Settle messages explicitly: complete, abandon or dead-letter.
- Design handlers for at-least-once delivery: idempotent, with poison messages isolated.
:::

## The business problem

LedgerOne must know every customer Harbourline has: when an account is created or its billing details change in Dynamics 365, LedgerOne needs the change. On month-end days LedgerOne slows to several seconds per call, and twice a year it is down for maintenance for an hour.

A direct call from Dataverse would fail on those days, and the change would be gone. Harbourline needs a buffer that holds changes until LedgerOne can take them, and a place where the few changes that can never be processed wait for a person.

## How it works

**Azure Service Bus** is a message broker. Senders put **messages** on it; receivers take them off, in their own time.

A **queue** delivers each message to one receiver. A **topic** delivers a copy of each message to every **subscription**, and each subscription behaves like its own queue (@fig:topic). Harbourline uses a topic for account events because more consumers will follow LedgerOne: an audit store, a marketing system.

![Dataverse publishes each account change once; every subscription gets its own copy, and messages that keep failing move to that subscription's dead-letter queue.](figures/ch11/topic.svg){#fig:topic}

### Peek-lock and settlement

Functions receive messages in **peek-lock** mode. The message is locked for a period (the *lock duration*, one minute by default) and stays on the queue until the receiver **settles** it:

- **Complete** removes it: processing succeeded.
- **Abandon** releases the lock: Service Bus delivers it again, and increments its **delivery count**.
- **Dead-letter** moves it to the **dead-letter queue (DLQ)**, a sub-queue where it waits for inspection.

If the receiver crashes or the lock expires, the message becomes visible again. When the delivery count reaches the entity's **max delivery count** (10 by default), Service Bus dead-letters it automatically.

### At-least-once delivery

This design guarantees that a message is processed *at least* once, not *exactly* once. A function can call LedgerOne successfully and crash before completing the message; the message is then delivered again. Every handler must therefore be **idempotent**, the theme of Chapter 8: here, an upsert to LedgerOne with the message ID as idempotency key.

### Other features worth knowing

- **Duplicate detection** discards a message whose `MessageId` was seen within a configured window. It protects against *senders* that retry; Chapter 24 uses it with the order number as message ID.
- **Sessions** give ordered, first-in-first-out processing per session ID, for example per customer. Use them when order matters for the same entity.
- **Topic filters** let a subscription receive only some messages, by properties on the message.

### How Dataverse posts to Service Bus

A **service endpoint** registered in Dataverse names a Service Bus queue or topic, a **message format** (.NET binary, JSON or XML), and a SAS key to authenticate. Steps registered against it run asynchronously and post the RemoteExecutionContext, the same context as a webhook (Chapter 9). With the JSON format, the reader you built in Chapter 9 parses it. Dataverse also adds properties to each message identifying the organization, the user, the table and the message; inspect them in Service Bus Explorer before writing filters on them.

When the payload exceeds 192 KB, Dataverse removes the parameters and images and marks the message; if it's still too large, the post fails.

## Architecture

::: {#tbl:components caption="Components in @fig:topic"}
| Component | Role | Authenticates with | Failure path |
|---|---|---|---|
| Dataverse service endpoint | Posts account changes | SAS rule with **Send** right on this topic only | Failed System Job |
| Topic `account-events` | Buffers and fans out | — | Messages expire per time-to-live |
| Subscription `ledgerone` | LedgerOne's copy | — | Auto-dead-letter after 10 deliveries |
| `AccountEventsToLedgerOne` | Sends to LedgerOne, settles | Managed identity, *Azure Service Bus Data Receiver* | Abandon (transient) or dead-letter (permanent) |
:::

## Prerequisites

- Chapter 8: the LedgerOne client and failure classification ideas.
- Chapter 9: `DataverseEvent` and the Plug-in Registration Tool.
- A Service Bus namespace on the **Standard** tier or higher; Basic has no topics.

## Step-by-step implementation

### Step 1 – Create the topic, subscription and Send-only rule

```{.bicep #lst:bicep include="infra/main.bicep" region="bicep-servicebus" caption="Service Bus entities in Bicep"}
```

The `dataverse-send` rule can send to `account-events` and do nothing else. Even if the key stored in Dataverse leaked, it couldn't read a message or touch another entity. The Function App doesn't use a key at all: Bicep gives its identity the *Azure Service Bus Data Receiver* and *Data Sender* roles (Chapter 20).

### Step 2 – Register the service endpoint

In the Plug-in Registration Tool choose **Register › Register New Service Endpoint**, and paste the `dataverse-send` rule's connection string. Set:

- **Designation type:** Topic, **Topic name:** `account-events`
- **Message format:** JSON
- **User information sent:** UserId

Then register steps on the endpoint for `Create` and `Update` of `account`, asynchronous, with filtering attributes `name`, `accountnumber`, `emailaddress1`, `telephone1` and `address1_city`, and a post-image with the same columns.

### Step 3 – Connect the function with its identity

The trigger's `Connection = "ServiceBus"` names a *prefix* of app settings. With `ServiceBus__fullyQualifiedNamespace` set to `<namespace>.servicebus.windows.net`, the extension authenticates with `DefaultAzureCredential`: your sign-in locally, the managed identity in Azure. No connection string exists anywhere in the app.

### Step 4 – Write the handler and settle explicitly

```{.csharp #lst:handler include="src/Harbourline.Connect.Functions/Messaging/AccountEventsFunction.cs" region="sb-function" caption="Process, then complete, abandon or dead-letter"}
```

1. `AutoCompleteMessages = false` hands settlement to your code. With auto-complete on, the host completes the message when the function returns and abandons it when it throws, which is fine for simple cases but can't dead-letter on purpose.
2. An account without a LedgerOne ID isn't a LedgerOne customer: complete and move on.
3. The message ID is stable across redeliveries, so LedgerOne recognises a repeat.
4. **Permanent failures** (a bad message, a 400 from LedgerOne, a permission error) can't be fixed by retrying; they go to the DLQ at once with a reason and description. Transient failures are dead-lettered only when this is the last allowed attempt.
5. **Transient failures** (timeouts, 429, 5xx, an open circuit) are abandoned, so Service Bus redelivers the message; the exception is rethrown so the failed attempt shows in monitoring.

The decision uses `FailureClassifier` (@lst:classifier), shared with the order pipeline in Chapter 24.

```{.csharp #lst:classifier include="src/Harbourline.Connect.Core/Orders/OrderProcessor.cs" region="failure-classifier" caption="Deciding whether a failure is worth retrying"}
```

1. Your own "this message is wrong" exception is always permanent.
2. No status code means the request never got an answer: a network problem, worth retrying.
3. Server errors, 429 and 408 are transient; other client errors (400, 401, 404) are permanent.
4. Dataverse throttling is transient. An unknown Dataverse fault is treated as transient too, so it is retried and, if it persists, still reaches the DLQ.
5. Anything unrecognised is retried rather than dropped: the max delivery count is the safety net.

### Step 5 – Tune concurrency

`host.json` controls how many messages one instance processes at once:

```{.json #lst:hostjson include="src/Harbourline.Connect.Functions/host.json" caption="host.json for Harbourline Connect"}
```

`maxConcurrentCalls` of 8 per instance keeps LedgerOne's load predictable; multiply it by the maximum instances to get the worst case, as in Chapter 2. `maxAutoLockRenewalDuration` renews the lock while a slow call is in progress, so a message isn't redelivered to another instance while the first is still working on it.

### Step 6 – Test the flow

Change an account's phone number in Dynamics 365. Within seconds the message appears on the subscription, the function runs, and the mock LedgerOne holds the new number. Start the mocks with `FailureRate=0.5` and watch messages being abandoned and redelivered, with rising delivery counts, until they succeed.

## Deep dive: locks, delivery counts and the dead-letter queue

Every reliability property of the account-events flow comes from a handful of Service Bus settings and the way the function settles each message. This section follows one message through its possible lives.

### The settings on the subscription

::: {#tbl:sbsettings caption="Service Bus settings used for Harbourline's entities"}
| Setting | `ledgerone` subscription | `order-intake` queue | What it does |
|---|---|---|---|
| Lock duration | 1 minute (default) | 1 minute | How long a received message stays invisible to other receivers |
| Max delivery count | 10 | 10 | Deliveries before Service Bus dead-letters the message itself |
| Duplicate detection | — | 1 hour, by `MessageId` | Drops a second message with the same ID inside the window |
:::

The values live in `infra/main.bicep`. The host adds one more: `maxAutoLockRenewalDuration` in `host.json` (10 minutes), which keeps renewing the lock while your function is still running, so a slow LedgerOne call doesn't lose the lock after one minute.

### One message, four endings

1. **Success.** The function calls `CompleteMessageAsync`. The message is deleted. This is the only ending that removes it for good, which is why `AutoCompleteMessages = false` and explicit settlement are worth the extra lines.
2. **Transient failure.** LedgerOne answers 503. The function calls `AbandonMessageAsync` and rethrows. The lock is released, the **delivery count** goes up by one, and the message is delivered again, often within seconds. Abandon has no built-in delay; the resilience pipeline's retries inside each delivery (Chapter 8) and the gap between deliveries together give the partner time to recover.
3. **Permanent failure.** The data is wrong, or the exception isn't transient. The function calls `DeadLetterMessageAsync` with a **reason** (the exception type) and a **description** (the message). The message moves to the subscription's dead-letter queue immediately, without wasting nine more deliveries.
4. **Too many attempts.** The function itself dead-letters a transient failure once `DeliveryCount` reaches `MaxAttempts`, which matches the subscription's max delivery count. If the function crashes without settling at all, the lock simply expires, and Service Bus dead-letters the message itself after the tenth delivery with the reason `MaxDeliveryCountExceeded`.

### Why "at least once" means "maybe twice"

Suppose the function upserts the customer in LedgerOne, and then the instance stops before `CompleteMessageAsync` reaches Service Bus. The lock expires and the same message is delivered again. LedgerOne sees the update twice. That's why the call passes `idempotencyKey: message.MessageId`, and why the operation is an upsert: doing it twice has the same result as doing it once. Design every handler on the assumption that it will, occasionally, process a message twice.

### Ordering

Service Bus doesn't promise that a subscription's messages are processed in order once you process them concurrently (`maxConcurrentCalls: 8`) or retry some of them. Two quick edits to the same account can reach LedgerOne in the wrong order. For customer master data, Harbourline accepts that risk because each message carries the **whole** synced record from the post-image, not a delta, and the weekly reconciliation (Chapter 21) catches the rare mismatch. When strict order per record matters, use **sessions** with the record ID as the session ID: messages with the same session ID are then processed one at a time, in order, while different records still run in parallel.

### What Dataverse adds to each message

Messages posted by Dataverse's Service Bus endpoint carry the `RemoteExecutionContext` in the body (as JSON in this book's registration) and copy key facts into **application properties**, such as the table's logical name and the message name, under names starting with `http://schemas.microsoft.com/xrm/2011/Claims/`. Subscription **filters** can use those properties to route messages without reading bodies: for example, a second subscription that receives only `Create` messages for `account`.

### Draining a dead-letter queue

A dead-letter queue is a to-do list, not an archive. Harbourline's rule: every dead-lettered message gets an owner within one working day. The usual decisions are:

- **Fix the data and replay.** The Ops tool's `dlq-replay` command (Chapter 24) copies messages back to the main entity, optionally filtered by reason.
- **Fix the code and replay.** After a deployment that handles a new case.
- **Discard deliberately.** For test data or events that no longer matter, after recording why.

The operations workbook (Chapter 18) shows dead-letter depth per entity, and an alert fires when it's above zero for more than an hour.

## Screens you will use

::: {.capture #fig:sb-explorer caption="Service Bus Explorer shows the message Dataverse posted, with its properties and JSON body." app="Azure portal" path="Service Bus namespace › Topics › account-events › Subscriptions › ledgerone › Service Bus Explorer › Peek" annotations="1 Message ID; 2 Delivery count; 3 Application properties added by Dataverse; 4 JSON body"}
:::

::: {.capture #fig:dlq caption="A dead-lettered message keeps the reason and description the function gave." app="Azure portal" path="… › ledgerone › Service Bus Explorer › Dead-letter" annotations="1 DeadLetterReason; 2 DeadLetterErrorDescription; 3 Enqueued time"}
:::

## Complete working code

- `infra/main.bicep`: namespace, topic, subscription, Send-only rule, role assignments.
- `src/Harbourline.Connect.Functions/Messaging/AccountEventsFunction.cs`
- `src/Harbourline.Connect.Core/Orders/OrderProcessor.cs`: `FailureClassifier`.

## Configuration

::: {#tbl:settings caption="Service Bus settings"}
| Setting | Local | Azure | Secret? |
|---|---|---|---|
| `ServiceBus__fullyQualifiedNamespace` | Dev namespace | Per environment | No |
| `host.json` `serviceBus.maxConcurrentCalls` | 8 | 8 | No |
| Dataverse endpoint connection string | — | Stored in Dataverse, Send-only rule | Yes |
:::

## Sample input and output

A dead-lettered message after LedgerOne rejected the data (fictional):

```text
MessageId:                  7f3c2a10e5b84f0aa1d9c0b6e2f4d311
DeliveryCount:              1
DeadLetterReason:           HttpRequestException
DeadLetterErrorDescription: Response status code does not indicate
                            success: 400 (Bad Request).
```

## Testing

`OrderTests.Failures_AreClassified` covers the classifier with each kind of failure. The settlement logic is best tested against a real namespace in your development subscription: send a message that LedgerOne's mock rejects and check that it reaches the DLQ after one delivery; stop the mock and check that messages are retried and recover when it returns.

## Common errors and troubleshooting

::: {#tbl:errors caption="Errors you may see with Service Bus"}
| Symptom | Likely cause | Fix |
|---|---|---|
| `Unauthorized` / claim 'Listen' required | Identity lacks Data Receiver role | Assign the role on the namespace or entity |
| Messages processed twice | At-least-once delivery after a crash or lock loss | Make the handler idempotent |
| `MessageLockLostException` | Processing longer than the lock, no renewal | Raise `maxAutoLockRenewalDuration` or speed up |
| Every message dead-lettered after 10 deliveries | A permanent failure treated as transient | Classify it as permanent; fix the data |
| Dataverse System Job: endpoint failed | Wrong SAS key or topic name | Re-register the endpoint |
| Basic tier: topics unavailable | Wrong tier | Use Standard or Premium |
:::

## Security considerations

::: security
The only key in this design is the SAS rule Dataverse uses. Scope it to **Send** on one topic, and rotate it with the rule's secondary key. Everything else uses managed identities and Azure roles; consider disabling local (SAS) authentication on namespaces that Dataverse doesn't write to.
:::

## Performance and scalability

::: performance
Flex Consumption scales Service Bus–triggered functions on queue length. Throughput is then limited by the slowest system downstream, so control concurrency deliberately: per-instance `maxConcurrentCalls`, and the app's maximum instance count. A queue that grows during LedgerOne's month-end slowdown and drains overnight is the design working, not failing.
:::

## Alternatives and design decisions

**Event Grid** delivers events push-style with retries and is ideal for Azure resource events, but it doesn't buffer for hours or offer peek-lock settlement. **Event Hubs** handles very high volumes of telemetry-like events with ordered partitions, but has no per-message dead-lettering. For business messages that must not be lost and may need a person to look at them, Service Bus fits.

A queue instead of a topic would work today, with one consumer. The topic costs nothing extra on the Standard tier and lets the next consumer subscribe without touching Dataverse.

## Best practices

- Use managed identities for functions; give Dataverse a Send-only rule on one entity.
- Settle explicitly when you need to dead-letter on purpose.
- Dead-letter permanent failures at once, with a reason; retry transient ones.
- Make handlers idempotent; use the message ID as idempotency key.
- Control concurrency with `maxConcurrentCalls` and maximum instances.
- Alert on dead-letter queue depth (Chapter 18) and have a replay tool ready (Chapter 24).

## Guided lab: follow account changes through the topic

**Requires:** the topic, subscription and service endpoint from Steps 1 and 2 in your development environment, the Functions host running locally with `ServiceBus__fullyQualifiedNamespace` set, and your user given the *Azure Service Bus Data Owner* role on the namespace (for the portal's explorer).

### Lab 11.1 – Watch a change arrive

Stop the Functions host so nothing consumes messages. In Dynamics 365, change the phone number of an account that has an account number (for example the fictional *Northwind Lettings*, `C-1001`). Then open the subscription in the Azure portal: **Service Bus namespace › Topics › account-events › Subscriptions › ledgerone › Service Bus Explorer** and choose **Peek from start**.

**Checkpoint:** one active message. Its application properties include the entity logical name `account`, and its body is a JSON execution context whose post-image contains the new phone number.

### Lab 11.2 – Process it

Start the mocks (no failures) and the Functions host. **Checkpoint:** within seconds the host log shows `AccountEventsToLedgerOne` executing and succeeding, and peeking the subscription again shows no active messages. Confirm the change reached the mock:

```bash
curl -s http://localhost:5080/ledgerone/customers/C-1001 \
     -H "x-api-key: local-development-key"
```

The returned customer has the new phone number.

### Lab 11.3 – A partner outage

Restart the mocks with `--FailureRate=1` (every request fails), then change the account's phone number again.

**Checkpoint:** the host log shows repeated attempts, and each new delivery of the same `MessageId` has a higher delivery count. While this is happening, peek the subscription: the message shows its current **delivery count**. Before it reaches ten, restart the mocks with `--FailureRate=0`. The next delivery succeeds and the message is completed. A short partner outage cost nothing but a delay.

### Lab 11.4 – A longer outage

Repeat Lab 11.3, but leave the failure rate at 1 until the attempts stop.

**Checkpoint:** the message is now in the subscription's **dead-letter** tab, with a reason such as `HttpRequestException` and a description mentioning the 503 status. The active count is zero: other accounts' changes aren't blocked behind the failed one.

### Lab 11.5 – Bad data

Restore the mocks to `--FailureRate=0`. In the Service Bus Explorer for the **topic**, send a message by hand with the body `not json` (the explorer's **Send messages** tab).

**Checkpoint:** the message is dead-lettered on its **first** delivery with the reason `JsonException`, not retried ten times. Permanent failures go straight to the dead-letter queue.

### Lab 11.6 – Resubmit from the dead-letter queue

In the explorer's dead-letter tab, select the message from Lab 11.4 and send it again (the explorer's dead-letter view offers a re-send action for selected messages). **Checkpoint:** the function processes it successfully, and LedgerOne shows the latest phone number. Remove the `not json` message from Lab 11.5 (receive it in the dead-letter view) instead of re-sending it: some messages should never be replayed.

### Lab 11.7 – Clean up

Leave the mocks at failure rate 0. Check that both the active and dead-letter counts of the `ledgerone` subscription are zero before you move on.

## Hands-on exercise

::: exercise
**Goal:** add a second subscriber.

**Steps:** add an `audit` subscription to `account-events` in Bicep, and a function that writes each account change's table, ID and correlation ID to the log.

**Done when:** one account change produces two function runs, one per subscription, and stopping the LedgerOne mock doesn't affect the audit function.

**Stretch:** add a SQL filter so the audit subscription only receives `Update` messages. Use the property names you see in Service Bus Explorer.
:::

## Knowledge check

::: knowledge-check
1. What is the difference between a queue and a topic?
2. What happens to a message whose function throws, when auto-complete is off and the code abandons it?
3. Why must handlers be idempotent even though Service Bus is reliable?
4. Which failures should be dead-lettered at once, and why?
5. What can the Dataverse SAS rule do in Harbourline's design, and why so little?
:::

Answers are in Appendix G.

::: summary
- Service Bus buffers messages, so slow or unavailable receivers don't lose data.
- Topics fan out each message to every subscription; Harbourline's account events use one.
- Peek-lock delivery means at least once: complete, abandon or dead-letter, and be idempotent.
- Permanent failures go to the dead-letter queue at once; transient ones are retried.
- Functions use managed identities; Dataverse gets a Send-only key for one topic.
:::

## References

- [Service Bus queues, topics, and subscriptions](https://learn.microsoft.com/azure/service-bus-messaging/service-bus-queues-topics-subscriptions) – Microsoft Learn, checked 2026-10-27.
- [Overview of Service Bus dead-letter queues](https://learn.microsoft.com/azure/service-bus-messaging/service-bus-dead-letter-queues) – Microsoft Learn, checked 2026-10-27.
- [Azure Service Bus trigger for Azure Functions](https://learn.microsoft.com/azure/azure-functions/functions-bindings-service-bus-trigger) – Microsoft Learn, checked 2026-10-27.
- [Azure integration (Dataverse)](https://learn.microsoft.com/power-apps/developer/data-platform/azure-integration) – Microsoft Learn, checked 2026-10-27.
- [Write a listener application for an Azure solution](https://learn.microsoft.com/power-apps/developer/data-platform/write-listener-application-azure-solution) – Microsoft Learn, checked 2026-10-27.
- [Duplicate detection](https://learn.microsoft.com/azure/service-bus-messaging/duplicate-detection) – Microsoft Learn, checked 2026-10-27.

<!-- Glossary terms introduced in this chapter: message broker, queue, topic, subscription, peek-lock, settlement, dead-letter queue, delivery count, max delivery count, at-least-once delivery, duplicate detection, session, service endpoint -->

<!-- expanded: deep dive + guided lab -->
