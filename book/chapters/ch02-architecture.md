---
chapter: 2
part: "Part I — Foundations"
title: "Azure Functions Architecture and Core Concepts"
build-summary: "You learn what actually runs when a function fires, how the platform scales it, which limits apply, and you choose the hosting plan Harbourline Connect will use."
builds:
  - "A compiled trigger gallery: HTTP, timer, Service Bus, Blob and multiple outputs"
  - "A capacity check that compares function concurrency with Dataverse's limits"
  - "Harbourline's hosting-plan decision, recorded with its reasons"
versions: ".NET 10 · Azure Functions 4.x isolated worker · Flex Consumption · Worker 2.52.0 · Timer 4.3.1 · Service Bus 5.24.0 · Storage Blobs 6.8.2"
---

::: objectives
- Name the parts of a Function App and explain how a trigger, a binding and a function relate.
- Describe the host and the isolated worker, and trace one invocation through both.
- Explain how Flex Consumption scales, per function group, and how concurrency multiplies load on Dataverse.
- State the timeouts and limits that shape integration design, including the 230-second HTTP limit.
- Choose a hosting plan using three questions, and justify Flex Consumption for Harbourline.
:::

## The business problem

Harbourline's old integrations ran on one virtual machine. It never scaled: on the first working day of each month, invoice volume tripled and the nightly job ran into the morning. It also never scaled down, so the company paid for an idle server three weeks out of four.

Priya's team has chosen Azure Functions (Chapter 1). Before writing integration code, the team needs answers to practical questions. What gets deployed? How many copies of the code will run at once, and what happens to Dataverse when they do? How long may a function run? Which plan should they pay for?

This chapter answers each question and ends with a recorded decision that every later chapter builds on.

## How it works

### Function apps, functions, triggers and bindings

A **Function App** is the unit you deploy and configure: one set of code, one set of app settings, one identity and one hosting plan. Inside it are **functions**: methods marked with the `[Function]` attribute.

Every function has exactly one **trigger**. In Microsoft's words, "a function must have exactly one trigger", and the trigger "defines how a function is invoked". **Bindings** are optional, declarative connections to other services: an *input binding* passes data in, and an *output binding* writes data out, so you write less plumbing code.

@tbl:triggers lists the triggers this book uses. Each package name starts with `Microsoft.Azure.Functions.Worker.Extensions`.

::: {#tbl:triggers caption="Triggers used in Harbourline Connect"}
| Trigger | Package suffix | Harbourline use | Chapters |
|---|---|---|---|
| HTTP | `.Http.AspNetCore` | APIs for the portal, webhooks from Dataverse and SignBridge | 6, 7, 9, 14 |
| Timer | `.Timer` | Nightly delta sync with LedgerOne | 10 |
| Service Bus | `.ServiceBus` | Dataverse events and order processing | 11, 24 |
| Blob (Event Grid source) | `.Storage.Blobs` | Property files from the partner feed | 13 |
| Durable orchestration | `.DurableTask` | Lease activation workflow | 12 |
:::

There is no Dataverse binding. Every function in this book talks to Dataverse through the Dataverse SDK, with a client registered once at start-up (Chapter 6).

### The host and the isolated worker

When a function runs, two processes cooperate, as @fig:anatomy shows.

![The Functions host listens to triggers and runs bindings; your code runs in a separate .NET worker process that talks to the host over gRPC.](figures/ch02/anatomy.svg){#fig:anatomy}

The **Functions host** is the runtime that Azure provides and updates. It listens to triggers, runs bindings, reads `host.json`, and reports load to the platform so it can scale.

Your code runs in a separate **isolated worker** process. Microsoft lists four benefits of this model: fewer assembly conflicts with the host, full control of start-up, standard .NET dependency injection and middleware, and freedom to target .NET versions the host does not use itself. For .NET 10 the isolated worker is the only model; the older **in-process** model loses support on 10 November 2026.

You own the worker's start-up. @lst:program is Harbourline Connect's `Program.cs`.

```{.csharp #lst:program include="src/Harbourline.Connect.Functions/Program.cs" region="program" caption="The worker's start-up: one builder, one host"}
```

1. `FunctionsApplication.CreateBuilder` creates the worker's host builder with the Functions defaults, including logging integration and the gRPC connection to the Functions host.
2. `ConfigureFunctionsWebApplication` enables ASP.NET Core integration, so HTTP functions use `HttpRequest` and `IActionResult`.
3. Everything the functions need is registered here, once. Chapter 6 explains what `AddHarbourlineCore` adds.
4. `Run` starts the worker. From here, the host calls in whenever a trigger fires.

A function is an ordinary class. @lst:health is the simplest function in the solution.

```{.csharp #lst:health include="src/Harbourline.Connect.Functions/Functions/HealthFunction.cs" region="health-function" caption="An HTTP function with a dependency injected through its constructor"}
```

1. Dependencies arrive through the primary constructor, resolved from the services registered in @lst:program.
2. The `[Function]` name is the function's identity in Azure: in logs, metrics and the portal.
3. The trigger attribute declares the event (an HTTP GET) and the route `api/health`.
4. The return value becomes the HTTP response.

### The life of one invocation

Following one event through @fig:anatomy explains most of the behaviour you will see in later chapters.

1. An event arrives: an HTTP request, a timer tick, a queue message or a blob notification.
2. The host's trigger listener receives it and runs any input bindings.
3. The host calls the worker over gRPC, naming the function and passing the trigger data.
4. The worker creates your function class through dependency injection and calls the method.
5. Your method returns. The host runs output bindings with the return value.
6. The host completes the trigger: it sends the HTTP response, or tells Service Bus the message succeeded, or records the timer run.

If step 4 throws, what happens next depends on the trigger. An HTTP caller receives an error response. A Service Bus message is retried and eventually dead-lettered (Chapter 11). A timer simply waits for its next schedule. Knowing which applies is the first step in designing failure handling.

### Stateless by design

The platform can start a new instance, or remove one, at any moment. Microsoft's guidance is direct: "Functions should be stateless and idempotent if possible. Associate any required state information with your data." It also advises designing functions "with the ability to continue from a previous fail point during the next execution."

For Harbourline this means three rules that recur in every chapter:

- Nothing important is kept in memory between invocations. Checkpoints live in Dataverse or storage (Chapter 10).
- Every handler tolerates the same event twice. Dataverse alternate keys and upsert make writes repeatable (Chapter 16).
- Long work is split into steps that can resume: queues between steps, or Durable Functions (Chapter 12).

### How Flex Consumption scales

A platform component, the **scale controller**, watches event sources and decides how many instances each app needs. On Flex Consumption, scaling is **per function**: HTTP-triggered functions scale together as one group, Event Grid–based Blob functions as another, Durable Functions as a third, and every other function scales on its own instances. @fig:scaling shows Harbourline's functions mapped to those groups.

![On Flex Consumption, HTTP functions scale together, Blob functions together, and every other function on its own instances. Instance counts are illustrative.](figures/ch02/scaling.svg){#fig:scaling}

Three numbers matter for planning:

- **Scale-out rate.** HTTP triggers add at most one instance per second; other triggers at most one every 30 seconds. A sudden burst is absorbed over seconds or minutes, not instantly.
- **Maximum instances.** The default maximum is 100 instances per group; you can set it from 1 to 1,000, subject to a regional quota that defaults to 250 cores per subscription.
- **Scale to zero.** With no events, a group scales to zero instances and costs nothing. The first event afterwards waits for an instance to start: a **cold start**. **Always-ready** instances, configured per group, remove that wait at a baseline cost.

### Concurrency: why scale-out reaches Dataverse fast

One instance runs many invocations at the same time. On Flex Consumption the default number of concurrent HTTP invocations per instance depends on the instance memory size (@tbl:concurrency), and you can change it.

::: {#tbl:concurrency caption="Default HTTP concurrency per instance on Flex Consumption (.NET)"}
| Instance memory | Default HTTP concurrency |
|---|---|
| 512 MB | 4 |
| 2,048 MB | 16 |
| 4,096 MB | 32 |
:::

Now multiply. Ten instances of the HTTP group at 2,048 MB can run 10 × 16 = 160 requests at once. If each request calls Dataverse as the same application user, that is far beyond Dataverse's default service protection limit of 52 concurrent requests per user on each web server (Chapter 1). Dataverse answers the excess with HTTP 429 and a `Retry-After` delay.

This simple arithmetic is the most useful capacity check in this book. Before a function goes live, multiply its maximum instances by its concurrency and compare the result with the limits of every system it calls. Chapter 15 shows the tools that keep the numbers in balance: lower maximum instance counts, lower concurrency, batching and back-off.

### Timeouts and other limits

@tbl:limits collects the limits that shape integration design on Flex Consumption.

::: {#tbl:limits caption="Limits that shape design on Flex Consumption"}
| Limit | Value | Design response |
|---|---|---|
| Function timeout | 30 minutes by default, no enforced maximum | Set `functionTimeout` deliberately; split long work |
| HTTP response | 230 seconds on every plan (load balancer) | Return 202 Accepted and finish asynchronously (Chapter 12) |
| Grace period on scale-in | Up to 60 minutes for running executions | Make work resumable anyway |
| Host start-up | 30 seconds, not configurable | Keep `Program.cs` fast; no network calls at start-up |
| Instance memory | 512 MB, 2,048 MB or 4,096 MB | Stream large files (Chapter 13) |
| Apps per plan | One | Each Function App gets its own plan |
| Event-based triggers per app | The scale controller monitors at most 100 | Split very large apps |
| Timer time zone | UTC; `WEBSITE_TIME_ZONE` not supported on Flex | Write schedules in UTC |
:::

### Hosting options

Azure Functions offers five hosting options. @tbl:plans compares them on the points that matter for Dataverse integration.

::: {#tbl:plans caption="Azure Functions hosting options compared"}
| Plan | Scales to zero | Cold-start mitigation | OS | Deployment slots | Choose it when |
|---|---|---|---|---|---|
| **Flex Consumption** | Yes | Always-ready instances | Linux | No | Default for new serverless apps |
| Premium | No (at least one warm instance) | Prewarmed and always-ready instances | Windows, Linux | Yes | Near-constant load, Windows-only code, slots |
| Dedicated (App Service) | No | Always On | Windows, Linux | Yes | Spare App Service capacity, fixed cost |
| Container Apps | Yes | Minimum replicas | Linux | Revisions instead | Functions packaged as containers beside microservices |
| Consumption (legacy) | Yes | None | Windows (Linux retiring) | No | Existing apps only; Linux retires 30 Sep 2028 |
:::

Microsoft's guidance for new work is short: "For new serverless function apps, use the Flex Consumption plan."

## Architecture: Harbourline's hosting decision

@fig:plans turns @tbl:plans into three questions, asked in order.

![Three questions decide between Container Apps, Dedicated, Premium and the default, Flex Consumption.](figures/ch02/plans.svg){#fig:plans}

Priya's team answered them for Harbourline Connect:

1. **Must it run as a container?** No. The team has no container platform and no need for a custom image.
2. **Is there spare App Service capacity?** No. Harbourline has no App Service plans.
3. **Windows, slots or near-constant load?** No. The code is .NET 10 on Linux; traffic is spiky, heaviest at month end and quiet at night. The lack of deployment slots is handled with pipeline-based releases (Chapter 20).

The decision: **Flex Consumption, 2,048 MB instances, default maximum instances to start with**, revisited in Chapter 15 when real Dataverse throughput is measured. Write decisions like this down. When someone asks in a year why the plan was chosen, the reasons matter more than the answer.

## Prerequisites

- Chapter 1, for the vocabulary of Dataverse and plug-ins.
- To build the samples: the .NET 10 SDK (installed in Chapter 3) and the companion repository at tag `ch-02`.

## Step-by-step: reading the trigger gallery

The repository's `samples/Harbourline.Samples.Triggers` project holds one function per trigger type, compiled by CI on every change. It is not deployed; it exists so that every signature in this chapter is checked by the compiler.

### Step 1 – A timer trigger

```{.csharp #lst:timer include="samples/Harbourline.Samples.Triggers/TriggerGallery.cs" region="timer-trigger" caption="A timer trigger that runs at 02:00 UTC every day"}
```

1. The schedule is an **NCRONTAB** expression with six fields: second, minute, hour, day, month and day of week. `0 0 2 * * *` means 02:00:00 every day, in UTC.

Only one instance runs a timer function, even when the app has scaled out, and a new run doesn't start while the previous one is still running. Microsoft warns against setting `RunOnStartup` to `true` in production, because the function then also runs whenever the app starts or scales.

### Step 2 – A Service Bus trigger

```{.csharp #lst:servicebus include="samples/Harbourline.Samples.Triggers/TriggerGallery.cs" region="servicebus-trigger" caption="A Service Bus trigger receiving the SDK message type"}
```

1. `Connection` names a group of app settings, not a connection string. With `ServiceBus__fullyQualifiedNamespace` set, the trigger uses the Function App's managed identity (Chapter 11).
2. `ServiceBusReceivedMessage` is the Azure SDK's own type, so properties such as `MessageId` and `DeliveryCount` are available.
3. `DeliveryCount` above 1 means the message has been delivered before: the reason every handler must be idempotent.

### Step 3 – A Blob trigger with an Event Grid source

```{.csharp #lst:blob include="samples/Harbourline.Samples.Triggers/TriggerGallery.cs" region="blob-trigger" caption="A Blob trigger using the Event Grid source required on Flex Consumption"}
```

1. On Flex Consumption the Blob trigger supports only the Event Grid source. Without `Source = BlobTriggerSource.EventGrid`, the function does not fire there. Chapter 13 adds the Event Grid subscription.
2. `{name}` in the path binds the blob's name to the `name` parameter.
3. Reading the file as a stream keeps memory low, which matters with 512 MB to 4 GB instances.

### Step 4 – An HTTP trigger with two outputs

A function's return value goes to its output binding. To write to two outputs, return a type whose properties carry the binding attributes (@lst:multi and @lst:multitype).

```{.csharp #lst:multi include="samples/Harbourline.Samples.Triggers/TriggerGallery.cs" region="multi-output" caption="Returning a queue message and an HTTP response from one function"}
```

1. The lease ID goes to a Storage queue for the slow work.
2. The caller receives 202 Accepted at once, well within the 230-second HTTP limit.

```{.csharp #lst:multitype include="samples/Harbourline.Samples.Triggers/TriggerGallery.cs" region="multi-output-type" caption="The return type that carries both outputs"}
```

1. `[QueueOutput]` sends the property's value to the named queue.
2. `[HttpResult]` marks which property is the HTTP response when ASP.NET Core integration is used.

## Configuration

@lst:hostjson is the samples' `host.json`, which configures the host for the whole app.

```{.json #lst:hostjson include="samples/Harbourline.Samples.Triggers/host.json" caption="host.json with an explicit function timeout"}
```

Setting `functionTimeout` explicitly, even to the default, documents the decision. @tbl:settings lists the app settings the gallery needs.

::: {#tbl:settings caption="App settings used by the trigger gallery"}
| Setting | Purpose | Secret? |
|---|---|---|
| `AzureWebJobsStorage` | Host storage: locks, timer schedules, queue output | Use identity-based settings in Azure (`AzureWebJobsStorage__accountName`) |
| `FUNCTIONS_WORKER_RUNTIME` | `dotnet-isolated` | No |
| `ServiceBus__fullyQualifiedNamespace` | Service Bus namespace for the identity-based connection | No |
| `FeedStorage__blobServiceUri` | Storage account holding the property feed | No |
:::

## Testing

1. Build the solution: `dotnet build HarbourlineConnect.slnx`. A successful build proves the trigger signatures compile against the pinned packages.
2. Run the Harbourline Connect app locally with Core Tools (Chapter 3 shows how). Core Tools lists each function with its trigger type, and the URL of each HTTP function.
3. Call `GET http://localhost:7071/api/health`. Expect HTTP 200 with the service name, version and time.

## Common errors and troubleshooting

::: {#tbl:errors caption="Errors you may see with triggers and hosting"}
| Symptom | Likely cause | Fix |
|---|---|---|
| Timer fires at the "wrong" hour | Schedules are in UTC; `WEBSITE_TIME_ZONE` isn't supported on Flex | Convert the time to UTC in the expression |
| Blob function never fires on Flex | Trigger uses the default polling source | Set `Source = BlobTriggerSource.EventGrid` and add the event subscription |
| HTTP caller gets a timeout at about four minutes | 230-second HTTP limit | Return 202 and continue asynchronously |
| Host fails to start | `AzureWebJobsStorage` missing or start-up code slow | Add the setting; keep `Program.cs` free of network calls |
| Requests rejected under load | Maximum instance count set too low | Load-test, then raise the limit |
| Dataverse returns 429 under load | Instances × concurrency exceeds Dataverse limits | Lower concurrency or instances; batch and back off (Chapter 15) |
:::

## Security considerations

::: security
Use `AuthorizationLevel.Anonymous` only for endpoints that reveal nothing, such as the health check. Everything else starts at `Function` level (a key) and moves to Microsoft Entra ID in Chapter 14. Prefer identity-based connections (`__fullyQualifiedNamespace`, `__accountName`) over connection strings, so no storage or Service Bus key sits in app settings.
:::

## Performance and scalability

::: performance
Reuse clients: Microsoft advises creating "a single, shared client that every function invocation can reuse" rather than one per invocation. Give each Function App its own storage account. And run the capacity check from this chapter, instances × concurrency compared with downstream limits, before every go-live.
:::

## Alternatives and design decisions

@tbl:models compares the two .NET programming models you will meet in older code and samples.

::: {#tbl:models caption="Isolated worker versus in-process model"}
| Aspect | Isolated worker (this book) | In-process (legacy) |
|---|---|---|
| Process | Separate from the host | Inside the host |
| .NET versions | .NET 8, 9, 10 and .NET Framework 4.8.1 | .NET 8 only |
| Start-up and middleware | Full control in `Program.cs` | Limited `FunctionsStartup` class |
| Support | Current | Ends 10 November 2026 |
| Flex Consumption | Supported | Not supported |
:::

A second decision is how many Function Apps to create. Harbourline starts with one app, which is simplest to deploy and monitor. Split an app when parts need different scale limits, different identities or permissions, different release cadences, or when it approaches 100 event-based triggers.

## Best practices

- Keep `Program.cs` fast and free of network calls; the host gives start-up 30 seconds on Flex.
- Make every function stateless and idempotent; store checkpoints with your data.
- Write timer schedules in UTC and never enable `RunOnStartup` in production.
- Use the Event Grid source for Blob triggers.
- Before go-live, multiply maximum instances by concurrency and compare the result with Dataverse's limits.
- Record the hosting decision and its reasons alongside the code.

## Hands-on exercise

::: exercise
**Goal:** read and change trigger configuration.

**Steps:** in the trigger gallery, change the timer to run at 06:30 UTC on weekdays only. Add a second queue output to `SignatureRequestOutput` for an audit queue. Then list each function in the gallery with the Flex scale group it belongs to.

**Done when:** the solution builds, and your list puts the two HTTP functions in the `http` group, the Blob function in the `blob` group and the other two on their own.

**Stretch:** for a function whose group may scale to 20 instances at 2,048 MB, calculate the maximum concurrent Dataverse requests, and choose a concurrency setting that keeps it under 52.
:::

## Knowledge check

::: knowledge-check
1. How many triggers can a function have, and how many bindings?
2. Which process runs your code in the isolated worker model, and what runs bindings?
3. Which Harbourline functions share instances on Flex Consumption, and which scale alone?
4. Ten instances at 2,048 MB run an HTTP function that calls Dataverse. How many concurrent Dataverse calls can that produce by default, and why does it matter?
5. A partner's API takes up to six minutes to respond. Why can't an HTTP-triggered function simply wait for it?
:::

Answers are in Appendix G.

::: summary
- A Function App is the unit of deployment; each function has exactly one trigger and optional bindings.
- The Functions host runs triggers and bindings; your .NET 10 code runs in a separate isolated worker that you start in `Program.cs`.
- Flex Consumption scales per function group, from zero to a default of 100 instances, and each instance runs many invocations at once.
- Instances × concurrency can exceed Dataverse's limits quickly: check it before go-live.
- HTTP functions must answer within 230 seconds; long work goes asynchronous.
- Harbourline Connect runs on Flex Consumption at 2,048 MB, a decision recorded with its reasons.
:::

## References

- [Azure Functions triggers and bindings concepts](https://learn.microsoft.com/azure/azure-functions/functions-triggers-bindings) – Microsoft Learn, checked 2026-10-24.
- [Guide for running C# Azure Functions in the isolated worker model](https://learn.microsoft.com/azure/azure-functions/dotnet-isolated-process-guide) – Microsoft Learn, checked 2026-10-24.
- [Event-driven scaling in Azure Functions](https://learn.microsoft.com/azure/azure-functions/event-driven-scaling) – Microsoft Learn, checked 2026-10-24.
- [Flex Consumption plan hosting](https://learn.microsoft.com/azure/azure-functions/flex-consumption-plan) – Microsoft Learn, checked 2026-10-24.
- [Concurrency in Azure Functions](https://learn.microsoft.com/azure/azure-functions/functions-concurrency) – Microsoft Learn, checked 2026-10-24.
- [Azure Functions hosting options](https://learn.microsoft.com/azure/azure-functions/functions-scale) – Microsoft Learn, checked 2026-10-23.
- [Timer trigger for Azure Functions](https://learn.microsoft.com/azure/azure-functions/functions-bindings-timer) – Microsoft Learn, checked 2026-10-24.
- [Improve the performance and reliability of Azure Functions](https://learn.microsoft.com/azure/azure-functions/performance-reliability) – Microsoft Learn, checked 2026-10-24.
- [Manage connections in Azure Functions](https://learn.microsoft.com/azure/azure-functions/manage-connections) – Microsoft Learn, checked 2026-10-24.

<!-- Glossary terms introduced in this chapter: Function App, function,
binding, input binding, output binding, Functions host, isolated worker,
in-process model, scale controller, per-function scaling, cold start,
always-ready instance, concurrency, NCRONTAB -->
