---
chapter: 9
part: "Part III — Real-World Integration Patterns"
title: "Webhooks, Plug-ins, and Power Automate Integration"
build-summary: "When a lease is marked ready for signature in Dynamics 365, Dataverse calls Harbourline Connect within seconds through a webhook; the function checks the event, queues the slow work and answers at once."
builds:
  - "An asynchronous Dataverse webhook on hl_lease, secured with a function key"
  - "A webhook function that parses the execution context and queues a signing request"
  - "A Power Automate custom connector for the Property API"
versions: ".NET 10 · Functions 4.x isolated worker · Service Bus extension 5.24.0 · Plug-in Registration Tool"
---

::: objectives
- Compare webhooks, Service Bus endpoints, plug-ins that call out, and Power Automate as ways for Dataverse to call a function.
- Register a webhook and a step with filtering attributes and a post-image.
- Parse the RemoteExecutionContext and handle truncated payloads.
- Secure a webhook endpoint, and check that an event came from the expected environment.
- Choose synchronous or asynchronous execution and find failures in System Jobs.
- Expose an Azure Function to makers through a custom connector.
:::

## The business problem

When a lettings agent sets a lease to *Awaiting signature*, Harbourline wants SignBridge to email the tenant within a minute. Today an agent exports the lease to PDF and uploads it to SignBridge by hand, and leases wait days for someone to get round to it.

The trigger lives in Dynamics 365; the work lives in Azure. This chapter connects the two.

## How it works

Dataverse's **event framework** runs registered code when a message, such as `Create` or `Update`, is processed for a table. Each registration is a **step**: a message, a table, a stage and a mode. There are four ways for a step's event to reach code in Azure (@fig:paths, @tbl:paths).

![Four ways a Dataverse event can reach a function; Harbourline uses the asynchronous webhook and hands slow work to a queue.](figures/ch09/paths.svg){#fig:paths}

::: {#tbl:paths caption="Calling Azure from Dataverse"}
| Path | Sync? | Limits | Best for |
|---|---|---|---|
| **Webhook** | Sync or async | 60-second timeout; payload above 256 KB is trimmed | Low-to-moderate volume, any HTTPS endpoint |
| **Service Bus endpoint** | Async only | Payload above 192 KB is trimmed or rejected | High volume, buffering, many consumers (Chapter 11) |
| **Plug-in calling out** | Either | Whole plug-in must finish in 2 minutes | Logic that must shape the request or response |
| **Power Automate** | Async | Flow limits and licensing | Logic makers own and change |
:::

A **webhook** is the simplest: Dataverse sends an HTTP `POST` whose body is the JSON form of the **RemoteExecutionContext**, the same context a plug-in receives. It contains the message name, the table and row ID, the user, a correlation ID, the changed columns in `InputParameters["Target"]`, and any **entity images** registered on the step: snapshots of the row before (*pre-image*) or after (*post-image*) the operation.

Dataverse treats any 2xx response as success and ignores the body. For 502, 503 and 504 it makes one more attempt. For other failures, a synchronous step shows the user an error and rolls back the operation; an asynchronous step records the failure in **System Jobs**.

::: warning
In a synchronous step, the request leaves Dataverse before the transaction commits. If a later step fails and the operation rolls back, the request "can't be recalled", in Microsoft's words. Your function may act on a change that never happened. Use asynchronous steps unless you must block the save.
:::

## Architecture

::: {#tbl:components caption="Components in this chapter"}
| Component | Role | Authenticates with | Failure path |
|---|---|---|---|
| Webhook registration | Endpoint URL and auth for Dataverse | `WebhookKey`: the function key, sent as `code` | 401 from the host if the key is wrong |
| Step on `hl_lease` Update | Fires on status change, async, post-image | — | Failed System Job |
| `LeaseStatusWebhook` | Validates and queues | Function key (host); org ID check (code) | 400, 403, or 500 → System Job |
| `lease-signing` queue | Buffers signing requests | Managed identity | Retries; dead-letter (Chapter 11) |
:::

## Prerequisites

- Chapters 6 and 8: the app deployed; the SignBridge client ready.
- The Plug-in Registration Tool, installed with `pac tool prt`.
- A Service Bus namespace with a `lease-signing` queue (Bicep in Chapter 20 creates it; for now, create it in the portal).

## Step-by-step implementation

### Step 1 – Parse the execution context

`DataverseEvent` (@lst:parse) reads the parts of the context integrations need, with `System.Text.Json`. It doesn't need the SDK's serializer, so it works the same for webhooks and for JSON-format Service Bus messages.

```{.csharp #lst:parse include="src/Harbourline.Connect.Core/Events/DataverseEvent.cs" region="event-parse" caption="Reading the target and post-image from a RemoteExecutionContext"}
```

1. `Clone` copies the root element so it outlives the `JsonDocument`, which is disposed at the end of the method.
2. When the payload exceeds 256 KB, Dataverse removes `InputParameters` and the images and adds the `x-ms-dynamics-msg-size-exceeded` header. The reader returns an empty set rather than failing; `IsTruncated` lets callers notice and read the row from Dataverse instead.
3. Collections in the context are arrays of `key`/`value` pairs, not JSON objects.
4. The first matching entry wins.

```{.csharp #lst:values include="src/Harbourline.Connect.Core/Events/DataverseEvent.cs" region="event-values" caption="Typed accessors for choice, money and lookup values"}
```

1. Choices and money are objects with a `Value` property; lookups have `Id` and `LogicalName`.
2. A value missing from the target falls back to the post-image. The target holds only the changed columns, so the lease number comes from the image.

### Step 2 – Write the webhook function

```{.csharp #lst:webhook include="src/Harbourline.Connect.Functions/Webhooks/LeaseWebhookFunction.cs" region="webhook-function" caption="The webhook: check, queue, answer"}
```

1. `AuthorizationLevel.Function`: the host rejects requests without a valid key before your code runs.
2. A body that isn't a Dataverse context is the caller's fault: 400.
3. **Defence in depth.** The key proves the caller knows a secret; the organization ID proves the event came from *this* environment. A key copied into a test environment's webhook can't trigger production work.
4. Only the status change that matters starts work. Every other update is acknowledged and ignored.
5. Without the number, there is nothing to do; the log tells the administrator how to fix the step.
6. The signing request goes to a queue, and the function returns 200 immediately. SignBridge calls, retries and waiting happen in Chapter 12's workflow, far from the 60-second webhook timeout.

The output type pairs the HTTP response with a Service Bus message, the multiple-output pattern from Chapter 2.

### Step 3 – Register the webhook

Get the function's URL and key from the portal (**Functions › LeaseStatusWebhook › Get function URL**). In the Plug-in Registration Tool, connect to the environment and choose **Register › Register New Web Hook**:

- **Name:** `Harbourline lease status`
- **Endpoint URL:** `https://<app>.azurewebsites.net/api/webhooks/lease-status`
- **Authentication:** `WebhookKey`, value: the function key.

`WebhookKey` sends the value as the `code` query parameter, exactly where the Functions host looks for a key.

### Step 4 – Register the step and image

Select the webhook, then **Register New Step**:

- **Message:** `Update`; **Primary entity:** `hl_lease`
- **Filtering attributes:** `hl_leasestatus` only, so other edits don't call the function at all
- **Execution mode:** Asynchronous
- **Delete AsyncOperation if StatusCode = Successful:** checked, so successful System Jobs don't fill the table

Then register a **post-image** on the step named `PostImage`, with the column `hl_leasenumber`.

### Step 5 – Test it

Open a lease in the model-driven app and set its status to *Awaiting signature*. Within seconds, Application Insights shows a `LeaseStatusWebhook` invocation and the log line "Queued signing for lease …", and the `lease-signing` queue holds one message.

### Step 6 – Find failures

For asynchronous steps, failures appear in **Settings › System Jobs** (or Advanced Settings › System Jobs), filtered by the step's name, with the HTTP status and the response. Check this view first when a webhook seems to do nothing.

### Step 7 – Give makers the API through a custom connector

Power Automate makers can call Harbourline's APIs too, through a **custom connector** built from an OpenAPI definition. In Power Automate, open **Custom connectors › New custom connector › Import an OpenAPI file**, and choose `infra/apim/partner-api.openapi.yaml`, the contract Chapter 14 publishes through API Management. Set the host to your API Management gateway and the security to OAuth 2.0 with Microsoft Entra ID.

Makers then see actions such as **Search properties** in the flow designer. The rules stay in the API; the maker decides when to call it.

## Deep dive: what Dataverse sends, and what it doesn't promise

A webhook looks simple: Dataverse posts JSON, your function answers 200. The details of that exchange decide whether your integration is reliable, so it's worth reading the contract closely.

### The request

Every webhook request is a `POST` whose body is the `RemoteExecutionContext` serialized as JSON, plus a few headers that let you route or reject a request before parsing the body.

::: {#tbl:webhookheaders caption="Headers Dataverse adds to webhook requests"}
| Header | Contains | Use it to |
|---|---|---|
| `x-ms-dynamics-organization` | Domain name of the sending environment | Log which environment called |
| `x-ms-dynamics-entity-name` | Logical name of the table | Route or reject quickly |
| `x-ms-dynamics-request-name` | The message (`Create`, `Update`, …) | Route or reject quickly |
| `x-ms-correlation-request-id` | Correlation identifier used by the platform | Diagnose with Microsoft support |
| `x-ms-dynamics-msg-size-exceeded` | Present only when the payload exceeded 256 KB | Detect a trimmed context |
:::

Headers are convenient, but don't use them for security decisions: anyone with the URL and key can set any header. The function in Step 2 checks the `OrganizationId` **inside** the body against configuration, and still relies on the key (or, better, a network restriction) to keep strangers out.

### The body

The fields you'll use most are `MessageName`, `PrimaryEntityName`, `PrimaryEntityId`, `OrganizationId`, `InitiatingUserId`, `CorrelationId`, `Depth`, and the collections `InputParameters`, `PreEntityImages` and `PostEntityImages`. Collections are serialized as arrays of `{ "key": …, "value": … }` pairs rather than JSON objects, and typed values carry their own shape: a choice is `{ "Value": 100000001 }`, a lookup is an object with `Id` and `LogicalName`. `DataverseEvent` in `src/Harbourline.Connect.Core/Events/DataverseEvent.cs` hides those shapes behind `GetChoice`, `GetString` and friends.

### Large payloads lose their data

When the whole payload exceeds 256 KB, Dataverse adds the `x-ms-dynamics-msg-size-exceeded` header and **removes** `ParentContext`, `InputParameters`, `PreEntityImages` and `PostEntityImages`. Your function then knows *that* a row changed, but not *what* changed. Two defences:

- Keep images small: register the post-image with only the columns you need (Step 4), never "all columns".
- When the collections are missing, read the row from Dataverse by `PrimaryEntityId` before acting. The parser in this chapter returns `null` values rather than throwing, so the function can choose to fall back.

### Delivery: once, maybe twice, never guaranteed

For an **asynchronous** step, the request is made by a system job. Dataverse waits up to **60 seconds** for a response. A 2xx response is success. A **502, 503 or 504** gets exactly one more attempt; any other failure, or a timeout, fails the system job. That has three consequences:

1. **Answer fast.** Validate, enqueue, return. Anything slow belongs behind the queue (this chapter's function only writes one Service Bus message).
2. **Expect duplicates.** One retry means your function may see the same event twice. The lease-signing workflow in Chapter 12 uses the lease number as the orchestration instance ID, so a duplicate can't start a second workflow.
3. **Don't rely on retries for outages.** If your function is down for ten minutes, events in that window fail, and you must replay them from System Jobs or reconcile later. When losing events is unacceptable, post to Service Bus instead (Chapter 11), which stores messages until you're ready.

::: {#tbl:webhookvsbus caption="Webhook or Service Bus endpoint?"}
| Need | Webhook | Service Bus endpoint |
|---|---|---|
| Lowest latency, simplest set-up | ✓ | |
| Survive a long outage of the receiver | | ✓ |
| Synchronous step that can block the save | ✓ (keep it very fast) | |
| Ordering and back-pressure | | ✓ (sessions, concurrency) |
| Receiver outside Azure | ✓ | |
:::

### Synchronous webhooks

A webhook can also be registered on a **synchronous** step. Then the user's save waits for your response, and a failure response cancels the operation with an error the user sees. That's powerful for validation against an external system, and dangerous for everything else: the user waits, and an outage in your function becomes an outage in Dynamics 365. Harbourline uses asynchronous steps for all webhooks in this book.

## Screens you will use

::: {.capture #fig:prt-webhook caption="Registering the webhook with WebhookKey authentication." app="Plug-in Registration Tool" path="Register › Register New Web Hook" annotations="1 Endpoint URL; 2 Authentication: WebhookKey; 3 Value: the function key"}
:::

::: {.capture #fig:prt-step caption="The step fires only when hl_leasestatus changes, asynchronously." app="Plug-in Registration Tool" path="Webhook › Register New Step" annotations="1 Message Update; 2 Primary entity hl_lease; 3 Filtering attributes; 4 Asynchronous; 5 Delete AsyncOperation if successful"}
:::

::: {.capture #fig:system-jobs caption="A failed webhook call recorded as a System Job." app="Dynamics 365" path="Advanced Settings › System Jobs" annotations="1 Status Reason: Failed; 2 Message with HTTP status; 3 Step name"}
:::

## Complete working code

- `src/Harbourline.Connect.Core/Events/DataverseEvent.cs`
- `src/Harbourline.Connect.Functions/Webhooks/LeaseWebhookFunction.cs`
- `tests/.../Events/DataverseEventTests.cs`: parses a sample context shaped like Microsoft's published example.

## Configuration

::: {#tbl:settings caption="Settings used in this chapter"}
| Setting | Local | Azure | Secret? |
|---|---|---|---|
| `Dataverse__OrganizationId` | Developer environment's org ID | Per environment | No |
| `ServiceBus__fullyQualifiedNamespace` | Your dev namespace | Per environment | No |
| Function key | Not enforced locally | Stored in the webhook registration | Yes |
:::

The organization ID is on the environment's page in the Power Platform admin center, and in every context as `OrganizationId`.

## Sample input and output

An abbreviated request as Dataverse sends it (fictional IDs):

```text
POST /api/webhooks/lease-status?code=<function-key>
x-ms-dynamics-organization: harbourline-dev.crm11.dynamics.com
x-ms-dynamics-entity-name: hl_lease
x-ms-dynamics-request-name: Update
x-ms-correlation-request-id: aaaa0000-bb11-2222-33cc-444444dddddd

{"MessageName":"Update","PrimaryEntityName":"hl_lease",
 "InputParameters":[{"key":"Target","value":{"Attributes":[
   {"key":"hl_leasestatus","value":{"Value":100000001}}]}}],
 "PostEntityImages":[{"key":"PostImage","value":{"Attributes":[
   {"key":"hl_leasenumber","value":"LSE-0042"}]}}], …}
```

Response: `200 OK`, empty body, and one message `LSE-0042` on `lease-signing`.

## Testing

`DataverseEventTests` parses a context modelled on the sample in Microsoft's webhook documentation and checks the changed choice, the post-image fallback and truncated payloads. For the function itself, send the same JSON with an `.http` file to the local host: once with the right organization ID (expect 200 and a queued message), once with another (expect 403).

## Common errors and troubleshooting

::: {#tbl:errors caption="Errors you may see in this chapter"}
| Symptom | Likely cause | Fix |
|---|---|---|
| System Job: 401 | Wrong or rotated function key | Update the webhook's key value |
| Function returns 403 | `Dataverse__OrganizationId` doesn't match | Set it for this environment |
| Log: "event has no lease number" | Post-image missing or lacks `hl_leasenumber` | Register the image with that column |
| Function called on every lease edit | No filtering attributes | Filter on `hl_leasestatus` |
| Users see "Endpoint unavailable" | Step is synchronous and the function failed | Make the step asynchronous |
| Context has no `InputParameters` | Payload over 256 KB (header `x-ms-dynamics-msg-size-exceeded`) | Read the row from Dataverse |
:::

## Security considerations

::: security
A webhook key is a shared secret stored in Dataverse. Rotate it by adding a second function key, updating the registration, then revoking the old key. Never use `AuthorizationLevel.Anonymous` for webhooks, and never trust the payload alone: check the organization ID, and re-read anything security-sensitive from Dataverse with the app's own identity.
:::

## Performance and scalability

::: performance
A webhook scales only as far as the endpoint behind it, as Microsoft notes. Keep the function fast (validate, queue, return) and let a queue absorb bursts. For sustained high volume, register a Service Bus endpoint instead (Chapter 11), which buffers on the Dataverse side too.
:::

## Alternatives and design decisions

Harbourline chose an asynchronous webhook plus a queue. A Service Bus endpoint would also work and buffers better, but needs a SAS key stored in Dataverse; the webhook needs only a function key and keeps the event in Harbourline's own code. A plug-in calling out would let the team shape the request, but adds a deployment artifact to Dataverse and runs inside the 2-minute plug-in limit. Power Automate would let makers change the logic, which Harbourline doesn't want for a contract-signing process.

When Dataverse needs to *call a named operation* rather than react to a data change, consider a **Custom API**: a message you define, such as `hl_RequestSignature`, that makers and code can call, and that a plug-in implements by calling Azure.

## Best practices

- Prefer asynchronous steps; use synchronous only to block an invalid save.
- Filter steps on the columns that matter, and add images for the values you need.
- Answer webhooks within seconds; queue the slow work.
- Check the organization ID as well as the key.
- Turn on "Delete AsyncOperation if successful", and watch failed System Jobs.
- Give makers APIs through custom connectors, not direct access to functions.

## Guided lab: send Dataverse events by hand

You can test every branch of the webhook function locally by posting the same JSON that Dataverse sends. **Requires:** the host running locally with `Dataverse__OrganizationId` set (Chapter 3). The final step also needs `ServiceBus__fullyQualifiedNamespace` pointing at your development namespace (Chapter 11).

### Lab 9.1 – Save a sample event

Create `lease-event.json` with your own organization ID (fictional values elsewhere):

```json
{
  "MessageName": "Update",
  "PrimaryEntityName": "hl_lease",
  "PrimaryEntityId": "3c1f7a52-0000-4a1e-9c00-000000000042",
  "OrganizationId": "<your-organization-id>",
  "CorrelationId": "5d7e1c90-0000-4b3e-8f00-000000000001",
  "OperationId": "6a2b3c4d-0000-4e5f-9a00-000000000002",
  "InitiatingUserId": "7b3c4d5e-0000-4f60-8b00-000000000003",
  "Depth": 1,
  "InputParameters": [ { "key": "Target", "value": { "Attributes": [
    { "key": "hl_leasestatus", "value": { "Value": 100000001 } } ] } } ],
  "PostEntityImages": [ { "key": "PostImage", "value": { "Attributes": [
    { "key": "hl_leasenumber", "value": "LSE-0042" } ] } } ]
}
```

`100000001` is *Awaiting signature* in `LeaseStatus`.

### Lab 9.2 – Not a Dataverse event

```bash
curl -i -X POST http://localhost:7071/api/webhooks/lease-status \
     -H "Content-Type: application/json" -d '{"hello":"world"}'
```

**Checkpoint:** `400` with the title *Not a Dataverse event*. A malformed body is rejected before anything else happens.

### Lab 9.3 – The wrong organization

Copy the file to `other-org.json`, change any one hexadecimal digit of its `OrganizationId`, and post the copy:

```bash
curl -i -X POST http://localhost:7071/api/webhooks/lease-status \
     -H "Content-Type: application/json" --data @other-org.json
```

**Checkpoint:** `403`, and the host log shows *Webhook from unexpected organization*. A key copied to another environment can't trigger Harbourline's production workflow.

### Lab 9.4 – A change the function ignores

Change the status value to `100000000` (*Draft*) and post again.

**Checkpoint:** `200 OK` and no message queued. Returning 200 for events you don't care about matters: a non-2xx answer would fail the System Job and, for 502–504, cause a pointless retry.

### Lab 9.5 – The event that matters

Post the original file:

```bash
curl -i -X POST http://localhost:7071/api/webhooks/lease-status \
     -H "Content-Type: application/json" --data @lease-event.json
```

**Checkpoint:** `200 OK`, a log line saying the lease was queued, and (with Service Bus configured) one message with body `LSE-0042` on the `lease-signing` queue. Peek it in the Service Bus Explorer in the Azure portal, or run the Durable workflow from Chapter 12, which consumes it.

### Lab 9.6 – Measure your response time

Dataverse waits at most 60 seconds, but you should aim for well under a second:

```bash
curl -s -o /dev/null -w "%{http_code} in %{time_total}s\n" -X POST \
  http://localhost:7071/api/webhooks/lease-status \
  -H "Content-Type: application/json" --data @lease-event.json
```

**Checkpoint:** a 200 in a fraction of a second after the first call. If you ever add work to this function, run this check again; anything that pushes the time towards seconds belongs behind the queue.

### Lab 9.7 – End to end (optional)

With the function deployed and the webhook and step registered (Steps 3 and 4), change a lease's status to *Awaiting signature* in Dynamics 365. Then open **Advanced Settings › System Jobs** and find the job for your step. **Checkpoint:** the job's status is *Succeeded* (or it has disappeared, if you chose to delete successful jobs in Step 4), and the function's invocation log shows one request.

## Hands-on exercise

::: exercise
**Goal:** react to a second event.

**Steps:** register a step on `Create` of `hl_invoice` and extend the function (or add a new one) to log the invoice number and amount from the target.

**Done when:** creating an invoice in the model-driven app logs both values, and an update to the invoice does not call the function.

**Stretch:** make the function return 503 when the queue is unreachable, and verify in System Jobs that Dataverse retried once.
:::

## Knowledge check

::: knowledge-check
1. What does a webhook's body contain, and where do you find the changed columns?
2. Why is a synchronous webhook risky even when it succeeds?
3. Which status codes make Dataverse retry a webhook, and how many times?
4. Why does the function check the organization ID when the host already checked the key?
5. When would you choose a Service Bus endpoint over a webhook?
:::

Answers are in Appendix G.

::: summary
- Webhooks, Service Bus endpoints, plug-ins and flows can all call Azure from Dataverse; each has limits.
- The webhook body is the RemoteExecutionContext; changed values are in the target, others in images.
- Secure webhooks with a function key and check the organization ID.
- Asynchronous steps avoid acting on rolled-back changes; failures land in System Jobs.
- Answer quickly and queue the work; custom connectors bring the same APIs to makers.
:::

## References

- [Use webhooks to create external handlers for server events](https://learn.microsoft.com/power-apps/developer/data-platform/use-webhooks) – Microsoft Learn, checked 2026-10-27.
- [Register a webhook](https://learn.microsoft.com/power-apps/developer/data-platform/register-web-hook) – Microsoft Learn, checked 2026-10-27.
- [Azure integration](https://learn.microsoft.com/power-apps/developer/data-platform/azure-integration) – Microsoft Learn, checked 2026-10-27.
- [Event framework](https://learn.microsoft.com/power-apps/developer/data-platform/event-framework) – Microsoft Learn, checked 2026-10-27.
- [Create and use Custom APIs](https://learn.microsoft.com/power-apps/developer/data-platform/custom-api) – Microsoft Learn, checked 2026-10-27.
- [Create a custom connector from an OpenAPI definition](https://learn.microsoft.com/connectors/custom-connectors/define-openapi-definition) – Microsoft Learn, checked 2026-10-27.

<!-- Glossary terms introduced in this chapter: event framework, step, webhook, RemoteExecutionContext, entity image, filtering attributes, System Jobs, custom connector, Custom API -->

<!-- expanded: deep dive + guided lab -->
