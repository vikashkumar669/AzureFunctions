---
chapter: 12
part: "Part III — Real-World Integration Patterns"
title: "Durable Functions and Long-Running Workflows"
build-summary: "Harbourline's lease-signing process runs as a durable workflow: it sends the envelope, waits days for the tenant, sends reminders, and activates or declines the lease, surviving restarts and deployments along the way."
builds:
  - "An orchestration that waits for an external event or a timer, with reminders"
  - "Activities that call SignBridge and Dataverse with retry policies"
  - "A signed SignBridge callback that resumes the right workflow"
versions: ".NET 10 · Durable Functions extension 1.20.0 · Durable Task Scheduler (AzureManaged 1.10.4)"
---

::: objectives
- Explain orchestrations, activities, clients and entities, and when to use Durable Functions.
- Write orchestrator code that replays deterministically.
- Wait for an external event with a timeout, and send reminders with durable timers.
- Retry activities with a retry policy, and keep workflows idempotent with instance IDs.
- Verify a partner's signed callback before resuming a workflow.
- Run and inspect workflows with the Durable Task Scheduler and its dashboard.
:::

## The business problem

A lease can wait days for a tenant's signature. Harbourline's process has steps and waits: send the envelope; if nothing happens for three days, remind; after two reminders, give up and put the lease back to draft; when the tenant signs or declines, update Dynamics 365.

A single function can't wait three days, and a timer job that polls every lease every hour is wasteful and hard to follow. Harbourline needs code that *reads* like the process, and pauses for free.

## How it works

**Durable Functions** is an extension of Azure Functions for stateful workflows written as ordinary code. It has four kinds of function:

- An **orchestrator function** describes the workflow: call this, then wait for that.
- **Activity functions** do the work: call SignBridge, update Dataverse. They are normal functions with all the usual services.
- A **client function** starts, queries or signals orchestrations, through a `DurableTaskClient`.
- **Entity functions** hold small pieces of state addressed by key. Harbourline doesn't need them.

### Replay and determinism

An orchestrator doesn't stay in memory while it waits. Each time it calls an activity, it records the call in a history and stops. When the activity finishes, the framework runs the orchestrator *again from the start*, feeding it the recorded results, until it reaches the next new step. This is **replay**.

Replay works only if the orchestrator makes the same decisions every time. Orchestrator code must therefore be **deterministic**:

- Use `context.CurrentUtcDateTime`, never `DateTime.UtcNow`.
- Do I/O, random numbers and new GUIDs only in activities.
- Wait with durable timers (`context.CreateTimer`), never `Task.Delay`.
- Don't read configuration or environment variables that can change between replays.

### Waiting for people

`context.WaitForExternalEvent<T>("name")` returns a task that completes when a client raises that event for this instance. Combined with a durable timer in `Task.WhenAny`, it gives "wait for the signature, or for three days, whichever comes first" (@fig:signing). While it waits, nothing runs and, on Flex Consumption, nothing is billed for compute.

![The orchestration sends the envelope, then waits for SignBridge's callback or a reminder timer, for up to nine days, without running any code while it waits.](figures/ch12/signing.svg){#fig:signing}

### Where state lives

The framework stores histories in a **storage provider**. Microsoft recommends the **Durable Task Scheduler**, a managed Azure service built for Durable Functions, with a dashboard for inspecting and managing orchestrations. It is configured in `host.json` with `"type": "azureManaged"` and a connection string, and comes in Dedicated and Consumption SKUs. Locally, it runs as a Docker emulator.

## Architecture

::: {#tbl:components caption="Components of the signing workflow"}
| Component | Role | Authenticates with | Failure path |
|---|---|---|---|
| `StartLeaseSigning` | Client: starts one workflow per lease | Service Bus with managed identity | Skips if already running |
| `LeaseSigningOrchestration` | The process | — | Fails after an activity exhausts retries |
| Activities | SignBridge and Dataverse calls | Managed identity | Retried 5 times, exponential backoff |
| `SignBridgeCallback` | Raises the event | HMAC signature from SignBridge | 401 if the signature is wrong |
| Durable Task Scheduler | Stores state, dispatches work | Managed identity, *Durable Task Data Contributor* | — |
:::

## Prerequisites

- Chapter 8: the SignBridge client and the mocks.
- Chapter 9: the webhook that queues `lease-signing` messages.
- Docker, to run the Durable Task Scheduler emulator.

## Step-by-step implementation

### Step 1 – Start the emulator

```bash
docker run --name dtsemulator -d -p 8080:8080 -p 8082:8082 \
  mcr.microsoft.com/dts/dts-emulator:latest
```

Port 8080 serves the app; the dashboard is at `http://localhost:8082`. The local connection string in `local.settings.sample.json` points at it: `Endpoint=http://localhost:8080;TaskHub=default;Authentication=None`. `host.json` selects the provider; Chapter 11 shows the full file.

### Step 2 – Write the orchestrator

```{.csharp #lst:orch include="src/Harbourline.Connect.Functions/Leases/LeaseSigning.cs" region="orchestrator" caption="The lease-signing orchestration"}
```

1. The input, the lease number, is part of the history, so it's the same on every replay.
2. A retry policy for activities: five attempts, 10 seconds then doubling. Activities fail on transient errors; the policy retries them without the orchestrator needing a loop.
3. Waiting for the event registers interest; it doesn't block.
4. The durable timer fires three days from the orchestration's own clock, `CurrentUtcDateTime`. The cancellation source lets the code cancel the timer when the event wins.
5. Cancel timers you no longer need: an orchestration doesn't complete while a timer is still pending.
6. After two reminders without an answer, the lease goes back to *Draft* and the workflow ends with the result `Expired`.

Read the code aloud and it is the process: send, wait, remind, decide. That is the main benefit of Durable Functions.

### Step 3 – Write the activities

```{.csharp #lst:activity include="src/Harbourline.Connect.Functions/Leases/LeaseSigning.cs" region="activity" caption="An activity: ordinary code with injected services"}
```

1. The lease number goes to SignBridge as the envelope's reference, so the callback can find the workflow again.

Activities are where nondeterminism belongs: network calls, the clock, logging with real timestamps. Each should be safe to run more than once, because a crash after the work but before the result is recorded means it *will* run again. `SetStatusAsync` is naturally idempotent; envelope creation is protected by the instance-ID rule in Step 4 and, with the Chapter 8 exercise, by an idempotency key.

### Step 4 – Start exactly one workflow per lease

```{.csharp #lst:starter include="src/Harbourline.Connect.Functions/Leases/LeaseSigning.cs" region="starter" caption="Starting one workflow per lease, idempotently"}
```

1. The **instance ID** is derived from the lease number, `lease-signing-LSE-0042`, so it's predictable.
2. If the webhook fires twice, or the message is redelivered, the second start finds a running instance and does nothing.
3. A custom instance ID also makes the workflow easy to find in the dashboard and to address from the callback.

### Step 5 – Resume the workflow from a signed callback

SignBridge calls Harbourline when a tenant signs. The endpoint must be anonymous at the host level, because SignBridge can't hold a function key, so the function verifies an **HMAC signature** instead: SignBridge computes HMAC-SHA256 of the body with a shared secret and sends it in a header.

```{.csharp #lst:hmac include="src/Harbourline.Connect.Core/Integrations/CallbackSignature.cs" region="hmac-verify" caption="Verifying an HMAC-signed callback"}
```

1. Compute the expected signature over the exact bytes received.
2. Compare in constant time, so an attacker can't learn the signature one byte at a time from response timings.

```{.csharp #lst:callback include="src/Harbourline.Connect.Functions/Leases/SignBridgeCallbackFunction.cs" region="callback" caption="The callback raises the event the orchestration waits for"}
```

1. Anonymous at the host; authenticated by signature in code.
2. The secret comes from Key Vault through a Key Vault reference.
3. A callback for a workflow that has already finished, for example a late decline after expiry, is acknowledged and ignored.
4. Raising the event resumes the orchestration at Step 2's `WhenAny`.

### Step 6 – Watch a workflow run

Set a lease to *Awaiting signature* (Chapter 9). Open the dashboard: the instance `lease-signing-LSE-0042` is *Running*, with `SendEnvelope` completed and a timer pending. Simulate SignBridge's callback with an `.http` request, signing the body with `CallbackSignature.Sign` and the local secret. The instance completes with `Active`, and the lease in Dynamics 365 shows *Active*.

To test reminders without waiting three days, set `ReminderEvery` to a few minutes in a development build.

## Screens you will use

::: {.capture #fig:dts-dashboard caption="The Durable Task Scheduler dashboard shows each lease's workflow and its history." app="Durable Task Scheduler dashboard" path="http://localhost:8082 › Task hub default › Orchestrations" annotations="1 Instance ID lease-signing-LSE-0042; 2 Status Running; 3 History: SendEnvelope completed; 4 Pending timer"}
:::

## Complete working code

- `src/Harbourline.Connect.Functions/Leases/LeaseSigning.cs`: orchestration, activities, starter.
- `src/Harbourline.Connect.Functions/Leases/SignBridgeCallbackFunction.cs`
- `src/Harbourline.Connect.Core/Leases/LeaseService.cs` and `Integrations/CallbackSignature.cs`
- `tests/.../Integrations/CallbackSignatureTests.cs`

## Configuration

::: {#tbl:settings caption="Durable Functions settings"}
| Setting | Local | Azure | Secret? |
|---|---|---|---|
| `DURABLE_TASK_SCHEDULER_CONNECTION_STRING` | Emulator, `Authentication=None` | Endpoint, task hub, `Authentication=ManagedIdentity` | No |
| `host.json` `durableTask.storageProvider.type` | `azureManaged` | `azureManaged` | No |
| `SignBridge__CallbackSecret` | Local value (32+ characters) | Key Vault reference | Yes |
:::

## Sample input and output

The callback SignBridge sends (fictional):

```text
POST /api/callbacks/signbridge
X-SignBridge-Signature: 9F0C…E41A
Content-Type: application/json

{"envelopeId":"ENV-7d2c91fa04","reference":"LSE-0042","status":"Signed"}

HTTP/1.1 202 Accepted
```

## Testing

`CallbackSignatureTests` proves that a correct signature passes and that a changed body, a missing signature or a malformed one fail. Orchestrator logic can be unit-tested with a mocked `TaskOrchestrationContext`, but replay behaviour is best checked against the emulator: start an instance, restart the host while it waits, and confirm it continues where it left off.

## Common errors and troubleshooting

::: {#tbl:errors caption="Errors you may see with Durable Functions"}
| Symptom | Likely cause | Fix |
|---|---|---|
| Orchestration behaves differently after a restart | Nondeterministic code (clock, GUID, I/O) in the orchestrator | Move it into an activity |
| Orchestration never completes after the event | Timer not cancelled | Cancel the timer when the event wins |
| Event raised but nothing happens | Wrong instance ID or event name | Use the same helper for IDs; constants for names |
| Two envelopes for one lease | Two instances for one lease | Derive instance IDs from the lease number |
| Connection refused on start-up locally | Emulator not running | Start the Docker container |
| 403 from the scheduler in Azure | Identity lacks *Durable Task Data Contributor* | Assign the role on the task hub |
:::

## Security considerations

::: security
Anonymous endpoints must authenticate the caller in code. HMAC signatures prove the body came from someone with the secret and wasn't changed; compare in constant time, and keep the secret in Key Vault. For extra protection against replays of old callbacks, partners often sign a timestamp too, and you reject old ones.
:::

## Performance and scalability

::: performance
Waiting costs no compute: an orchestration waiting three days for a signature runs for milliseconds per step. Keep orchestration inputs and outputs small (IDs, not documents), because every step's data is stored in the history and replayed.
:::

## Alternatives and design decisions

@tbl:alternatives compares ways to run a multi-day process.

::: {#tbl:alternatives caption="Running a long process"}
| Option | Strength | Weakness |
|---|---|---|
| **Durable Functions** (chosen) | Process as code, in the same app, testable | Determinism rules to learn |
| Logic Apps | Visual designer, many connectors | Logic outside the codebase; separate deployment |
| Power Automate approvals | Makers own it | Wrong owner for a contract process; licensing |
| Polling timer + status column | Simple | Wasteful, slow to react, logic scattered |
:::

When workflow code changes while instances are running, old instances replay against new code. With rolling updates on Flex Consumption, Microsoft recommends configuring an orchestration version match strategy. For small changes, make them compatible; for large ones, start new instances under a new orchestration name and let old ones finish.

## Best practices

- Keep orchestrators deterministic; do all I/O in activities.
- Use durable timers and `WaitForExternalEvent` for human waits; cancel timers you don't need.
- Derive instance IDs from business keys, and check before starting.
- Put retry policies on activities, and make activities idempotent.
- Keep inputs and outputs small.
- Authenticate callbacks to anonymous endpoints with signatures.

## Hands-on exercise

::: exercise
**Goal:** add an escalation step.

**Steps:** after the second reminder, before expiring, call a new activity that creates a Dataverse task for the lettings agent who owns the lease, then wait one more day.

**Done when:** in the emulator with short timers, an unanswered workflow creates exactly one task and then expires.

**Stretch:** let an agent cancel a workflow from Dynamics 365: a webhook raises a `Cancelled` event, and the orchestrator waits for it alongside the signature.
:::

## Knowledge check

::: knowledge-check
1. What happens to an orchestrator function's code when an activity completes?
2. Name three things an orchestrator must not do, and the alternative for each.
3. How does the workflow wait for the signature or three days, whichever comes first?
4. Why is the instance ID derived from the lease number?
5. Why is the callback endpoint anonymous, and what protects it?
:::

Answers are in Appendix G.

::: summary
- Durable Functions writes long processes as code: orchestrators decide, activities act.
- Replay requires deterministic orchestrators: durable timers, the context's clock, I/O only in activities.
- `WaitForExternalEvent` plus a durable timer models human waits at no compute cost.
- Instance IDs from business keys keep one workflow per lease.
- The Durable Task Scheduler stores state and provides a dashboard; signed callbacks resume workflows safely.
:::

## References

- [What are Durable Functions?](https://learn.microsoft.com/azure/durable-task/durable-functions/durable-functions-overview) – Microsoft Learn, checked 2026-10-27.
- [Orchestrator code constraints](https://learn.microsoft.com/azure/durable-task/common/durable-task-code-constraints) – Microsoft Learn, checked 2026-10-27.
- [Durable Task Scheduler](https://learn.microsoft.com/azure/durable-task/scheduler/durable-task-scheduler) – Microsoft Learn, checked 2026-10-27.
- [Quickstart: configure a Durable Functions app to use Durable Task Scheduler](https://learn.microsoft.com/azure/durable-task/scheduler/quickstart-durable-task-scheduler) – Microsoft Learn, checked 2026-10-27.
- [Handling external events](https://learn.microsoft.com/azure/durable-task/common/durable-task-external-events) – Microsoft Learn, checked 2026-10-27.
- [Rolling updates in Flex Consumption](https://learn.microsoft.com/azure/azure-functions/flex-consumption-site-updates) – Microsoft Learn, checked 2026-10-27.

<!-- Glossary terms introduced in this chapter: Durable Functions, orchestrator function, activity function, client function, entity function, replay, deterministic, durable timer, external event, instance ID, Durable Task Scheduler, HMAC -->
