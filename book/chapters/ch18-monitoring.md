---
chapter: 18
part: "Part IV — Advanced Enterprise Implementations"
title: "Logging, Monitoring, and Troubleshooting"
build-summary: "Every request, Dataverse call, log line and dead-lettered message from Harbourline Connect lands in Application Insights, joined by one trace ID; a workbook shows the health of every integration, and alerts reach the team before users do."
builds:
  - "OpenTelemetry for the host and the worker, exported to Application Insights"
  - "Structured, scoped logging and custom spans and metrics"
  - "KQL queries, an operations workbook and alert rules"
versions: ".NET 10 · Microsoft.Azure.Functions.Worker.OpenTelemetry 1.2.0 · Azure.Monitor.OpenTelemetry.Exporter 1.10.0 · host.json telemetryMode"
---

::: objectives
- Configure OpenTelemetry for both the Functions host and the isolated worker.
- Write structured logs with source-generated `LoggerMessage` methods and scopes.
- Add custom spans and metrics for business events.
- Follow one operation across functions, Dataverse, Service Bus and Durable Functions.
- Write KQL to answer "is it healthy?" and "what happened to this request?".
- Set up alerts and a workbook, and walk through root causes of common failures.
:::

## The business problem

On the first Monday after go-live, a lettings agent rang the service desk: a tenant hadn't received a lease to sign. Was the webhook called? Did the message reach the queue? Did SignBridge fail, or the workflow, or Dataverse? With the old integration, answering took a day of searching log files on a server.

Harbourline's operations goal: answer "what happened to lease LSE-0042?" in five minutes, and find out about failures before the service desk does.

## How it works

### OpenTelemetry in Azure Functions

**OpenTelemetry (OTel)** is a vendor-neutral standard for **traces** (operations made of **spans**), **metrics** and **logs**. Azure Functions supports it in two places, and you configure both:

- The **host** emits telemetry for triggers, invocations and bindings. Setting `"telemetryMode": "OpenTelemetry"` in `host.json` switches it to OTel.
- The **worker**, your process, emits your logs, your spans and the instrumented libraries' calls (HTTP, for example). `UseFunctionsWorkerDefaults()` connects it to the host's context, and an exporter sends the data on: `UseAzureMonitorExporter()` for Application Insights, or OTLP for other back ends.

Microsoft notes trade-offs: with `telemetryMode` set to OpenTelemetry, the `logging.applicationInsights` settings in `host.json` no longer apply, and the portal's log streaming isn't supported. The portal's invocation list works only when the data goes to Azure Monitor.

### Correlation

Every span carries a **trace ID**. Incoming HTTP requests continue the caller's trace if one is supplied (W3C `traceparent` header); otherwise a new one starts. HTTP calls to Dataverse and LedgerOne become child spans; Service Bus messages carry trace context to their consumers. In Application Insights the trace ID is the **operation ID**, and the end-to-end transaction view shows the whole tree.

Dataverse adds its own correlation: webhooks and Service Bus messages carry a `CorrelationId` (and the webhook header `x-ms-correlation-request-id`). Log it, and you can connect Harbourline's telemetry to Dataverse's plug-in traces and System Jobs.

### Structured logs

A **structured log** keeps values as named properties, not just text: `"Queued signing for lease {LeaseNumber}"` stores `LeaseNumber = LSE-0042` as a field you can filter on. **Source-generated logging** (`[LoggerMessage]`) does this with no allocation when the level is disabled, and the analyzers warn when you don't use it. **Scopes** add properties to every log written inside them, such as the invocation ID.

![The host and the worker both emit OpenTelemetry; one trace ID joins a request, its Dataverse calls, its logs and the messages it sends.](figures/ch18/telemetry.svg){#fig:telemetry}

## Architecture

::: {#tbl:components caption="Monitoring components"}
| Component | Role | Authenticates with | Failure path |
|---|---|---|---|
| Host OTel (`telemetryMode`) | Invocation telemetry | — | — |
| Worker OTel + Azure Monitor exporter | Logs, spans, metrics | Microsoft Entra ID (*Monitoring Metrics Publisher*) | Telemetry dropped if auth fails |
| Application Insights + Log Analytics | Store and query | Local auth disabled | — |
| Alert rules, action group | Notify the team | — | — |
| Workbook | Operations dashboard | Reader access | — |
:::

## Prerequisites

- Chapter 6 onwards: functions to observe.
- Application Insights created by Bicep (Chapter 20), with local authentication disabled.

## Step-by-step implementation

### Step 1 – Turn on OpenTelemetry in the host

Harbourline's `host.json` (shown in Chapter 11) sets `"telemetryMode": "OpenTelemetry"` and log levels under `logging.logLevel`.

### Step 2 – Configure the worker

```{.csharp #lst:otel include="src/Harbourline.Connect.Functions/HostingExtensions.cs" region="otel" caption="OpenTelemetry in the worker"}
```

1. A middleware (Step 4) adds invocation details to every log.
2. `UseFunctionsWorkerDefaults` links worker telemetry to the host's invocation spans.
3. Harbourline's own activity source and meter are added, so custom spans and metrics are exported too.
4. The exporter is added only when a connection string exists; locally, without one, telemetry stays in the console.

In Azure, `APPLICATIONINSIGHTS_AUTHENTICATION_STRING=Authorization=AAD` makes the exporter authenticate with the managed identity, which has *Monitoring Metrics Publisher* on the Application Insights resource. With local authentication disabled, telemetry sent with only the connection string's key is rejected, so nobody else can write fake telemetry into your logs.

### Step 3 – Log with source-generated methods

Every class in Harbourline Connect logs through partial methods like this one from the bulk loader:

```csharp
[LoggerMessage(Level = LogLevel.Information,
    Message = "Bulk load {Name}: {Rows} rows, {Succeeded} ok, …")]
private static partial void LogResult(ILogger logger, string name, …);
```

Rules the team follows: one event per meaningful step, never per row; business keys (`LeaseNumber`, `OrderNumber`) as properties; no personal data beyond IDs; and no secrets, tokens or full request bodies, ever.

### Step 4 – Add invocation context to every log

```{.csharp #lst:mw include="src/Harbourline.Connect.Functions/Middleware/CorrelationMiddleware.cs" region="middleware" caption="A worker middleware that opens a logging scope per invocation"}
```

1. The scope's properties appear on every log written during the invocation, by any class.
2. The trace ID ties logs to the distributed trace.
3. The middleware wraps the function: everything inside `next` is in scope.

### Step 5 – Add business spans and metrics

```{.csharp #lst:telemetry include="src/Harbourline.Connect.Core/Telemetry/HarbourlineTelemetry.cs" region="telemetry" caption="One activity source and one meter for Harbourline"}
```

1. An `ActivitySource` creates spans. `ProcessOrder` (Chapter 24) wraps each message in a span tagged with the order number and delivery count.
2. Counters and histograms record business measures: rows written, messages dead-lettered, batch durations. Units and descriptions make them self-explanatory in the portal.

### Step 6 – Query with KQL

The queries live in `infra/monitoring/queries.kql`, reviewed like code.

```{.kusto #lst:kqlhealth include="infra/monitoring/queries.kql" region="kql-health" caption="Health of every function over 24 hours"}
```

```{.kusto #lst:kqltrace include="infra/monitoring/queries.kql" region="kql-trace" caption="One operation, end to end"}
```

```{.kusto #lst:kqldv include="infra/monitoring/queries.kql" region="kql-dataverse" caption="Dataverse dependency volume, failures and latency"}
```

The file also has queries for one invocation's logs (by the middleware's `InvocationId`), bulk-load throughput and dead-letter counts.

### Step 7 – Alert

Harbourline starts with four alerts, each routed to an action group that emails the integration team and posts to its Teams channel:

1. **Function failures**: log alert on `requests | where success == false`, more than 5 in 15 minutes.
2. **Dead letters**: Service Bus metric *Dead-lettered messages* (`DeadletteredMessages`) above 0 on any entity.
3. **Dataverse throttling**: log alert on dependency failures with result code 429, or exceptions containing the throttling error codes.
4. **Sync stalled**: log alert when `NightlyInvoiceSync` has no successful run in 26 hours.

Each alert's description links to a runbook (Chapter 25) saying what to check first.

### Step 8 – Build the operations workbook

An Azure Monitor **workbook** combines queries and metrics on one page: invocations and failures per function, Dataverse latency, queue and dead-letter depth, the last run of each scheduled job. Save it in the resource group so it is deployed and versioned with the app.

### Step 9 – Walk through root causes

@tbl:rootcause is the team's first-look guide for the failures seen most often.

::: {#tbl:rootcause caption="Common exceptions and where to look first"}
| Exception or symptom | Likely root cause | First query |
|---|---|---|
| `FaultException` with `0x80040220` | Role missing a privilege after a solution change | Trace the operation; read the privilege name |
| `FaultException` with `0x80072322/21/26` | Throttling: parallelism too high or shared user busy | Dataverse dependency chart; bulk logs |
| `HttpRequestException` 503 → `BrokenCircuitException` | Partner down | Dependency failures by target |
| `MessageLockLostException` | Work longer than lock plus renewal | Invocation durations for the function |
| `OptionsValidationException` at start-up | Missing app setting after deployment | Host start-up traces |
| Orchestration stuck *Running* | Waiting for an event that never came | Durable Task Scheduler dashboard |
:::

## Deep dive: telemetry that pays for itself

Telemetry has a cost twice: the money to ingest and keep it, and the attention it takes to read it. Good monitoring spends both where they buy the most diagnosis. This section is about choosing what to collect, how long to keep it, and what to alert on.

### Know your four signals

::: {#tbl:signals caption="The four kinds of telemetry and what each is for"}
| Signal | Example in Harbourline | Answers | Cost driver |
|---|---|---|---|
| Traces (spans) | `ProcessOrder` request with Dataverse and LedgerOne dependencies | *Where did the time go in this operation?* | One record per span |
| Logs | *Invoice sync: 12 upserted, 0 failed…* | *What did the code decide, and why?* | One record per log line |
| Metrics | `harbourline.records.written` | *How much, how often, trending how?* | Cheap: pre-aggregated |
| Exceptions | `HttpRequestException` with stack trace | *What exactly failed?* | One record per exception, often large |
:::

Metrics are the cheapest way to answer "how many" questions, because they're aggregated before they're sent. Prefer a counter to counting log lines, and a histogram to calculating durations from traces, whenever the question is about volume or trends rather than individual operations.

### Log levels are a budget

Every `Information` log line in a function that runs a million times a day is a million records. Harbourline's rules:

- **Information**: one summary line per business operation (a sync run, a processed order), never per row.
- **Warning**: something unusual that the code handled (a retry, a skipped row, a late timer).
- **Error**: something that needs a person (a dead-lettered message, a failed run).
- **Debug**: off in production; switch on per category for an investigation, then off again.

Source-generated log methods (`[LoggerMessage]`, Step 3) cost almost nothing when their level is disabled, so detailed `Debug` logging can stay in the code safely.

### Sampling

At high volumes, keeping every trace is unnecessary: a representative fraction answers performance questions just as well. The Azure Monitor OpenTelemetry exporter supports sampling, configured in the worker. Two cautions: sampling decisions should be made per **operation**, so a sampled request keeps all its dependencies and logs together (the exporter's sampler does this); and **don't sample away failures** you need to investigate. Start without sampling, look at your ingestion volume after a week, and introduce sampling only if cost requires it.

### Retention and access

Application Insights data lives in the Log Analytics workspace; its retention is set there, per table if needed. Keep operational data long enough to compare this month with last month (90 days is a common choice), and send anything with a legal retention requirement (audit trails) to a store designed for it, not to monitoring. Telemetry contains business identifiers such as order numbers; give access through Azure roles on the workspace, and keep personal data (names, emails) out of logs altogether.

### Alerts people trust

An alert that fires for nothing teaches people to ignore alerts. Harbourline alerts only on symptoms that need action and that a person can act on:

- Dead-letter depth above zero for more than an hour.
- A scheduled job that hasn't succeeded within its expected window (no *Invoice sync* success line in 26 hours).
- Function failure rate above a threshold for 15 minutes.
- Dataverse throttling errors sustained for 15 minutes.

Each alert links to a runbook (Chapter 25) that says what to check first. Everything else goes on the workbook, to be looked at, not to wake someone up.

::: tip
Review alerts monthly: which fired, which led to action, which were noise. Delete or tune the noise. The goal is that every alert is worth reading.
:::

## Screens you will use

::: {.capture #fig:e2e caption="The end-to-end transaction view of one order: function, Dataverse calls, LedgerOne call and logs." app="Azure portal" path="Application Insights › Transaction search › select a request" annotations="1 ProcessOrder span; 2 Dataverse dependency; 3 LedgerOne dependency with retry; 4 Logs with OrderNumber"}
:::

::: {.capture #fig:workbook caption="The Harbourline operations workbook." app="Azure portal" path="Resource group › Workbooks › Harbourline operations" annotations="1 Failures per function; 2 Dataverse latency; 3 Dead-letter depth; 4 Last scheduled runs"}
:::

## Complete working code

- `src/Harbourline.Connect.Functions/HostingExtensions.cs`, `Middleware/CorrelationMiddleware.cs`, `host.json`
- `src/Harbourline.Connect.Core/Telemetry/HarbourlineTelemetry.cs`
- `infra/monitoring/queries.kql`
- `infra/main.bicep`: Application Insights with local auth disabled and the role assignment.

## Configuration

::: {#tbl:settings caption="Telemetry settings"}
| Setting | Value | Secret? |
|---|---|---|
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | From Bicep | No (identifies the resource; auth is Entra) |
| `APPLICATIONINSIGHTS_AUTHENTICATION_STRING` | `Authorization=AAD` | No |
| `host.json` `telemetryMode` | `OpenTelemetry` | No |
| `host.json` `logging.logLevel` | `Information` by default | No |
:::

## Sample input and output

The first query (@lst:kqlhealth) on a quiet day (illustrative):

```text
name                    total  failed  p95_ms  failure_rate
ProcessOrder              412       3     890           0.7
LeaseStatusWebhook         57       0      74           0
NightlyInvoiceSync          1       0   48211           0
```

## Testing

Telemetry is tested by looking. After each deployment, run one request of each kind (the smoke tests in Chapter 20), then open its end-to-end view: the function, its Dataverse calls and its logs must appear under one operation, with the business keys as properties. Fire each alert once in test, deliberately, and check that the email arrives and the runbook link works.

## Common errors and troubleshooting

::: {#tbl:errors caption="Errors you may see with monitoring"}
| Symptom | Likely cause | Fix |
|---|---|---|
| No telemetry at all | Exporter not authorised (local auth disabled) | Grant *Monitoring Metrics Publisher*; set the authentication string |
| Host telemetry but no worker logs | Worker OTel not configured | `UseFunctionsWorkerDefaults()` and an exporter |
| Logs missing custom properties | String interpolation instead of templates | Use `LoggerMessage` templates |
| Portal log stream empty | Not supported with OTel mode | Use Live Metrics or queries |
| Huge ingestion bill | Per-row logging or debug level in production | Log per step; raise levels; sample |
:::

## Security considerations

::: security
Logs are data, often kept for months and read by many people. Never log secrets, tokens, request bodies or personal data beyond identifiers. Disable local authentication on Application Insights so only your app's identity can send telemetry, and give support staff read access to logs without access to the data stores.
:::

## Performance and scalability

::: performance
Telemetry costs CPU, memory and money. Log per operation, not per row; use metrics for counts; keep `Information` as the production level; and use sampling when volume grows. Source-generated logging avoids work entirely when a level is off.
:::

## Alternatives and design decisions

The classic Application Insights SDK integration for isolated workers still exists, but OpenTelemetry is the vendor-neutral path and the one Microsoft is investing in for Functions. If Harbourline ever moves telemetry to another back end, only the exporter changes. Dataverse's own plug-in trace log and Application Insights integration for Dataverse are complementary: they show what happened *inside* Dataverse.

## Best practices

- Configure OTel in both host and worker; export with Entra authentication.
- Log structured events with business keys, through `LoggerMessage`.
- Add a scope per invocation; propagate trace context across messages.
- Keep KQL queries and workbooks in source control.
- Alert on failures, dead letters, throttling and missing scheduled runs; link each to a runbook.
- Never log secrets or bodies.

## Guided lab: find one order's story in the telemetry

**Requires:** the Function App deployed with Application Insights (Chapter 20's Bicep does this), the order flow from Chapter 24 deployed, and your user with *Reader* access to the Application Insights resource.

### Lab 18.1 – Create some traffic

Submit three orders through the deployed API, one of them for a property that doesn't exist:

```bash
for n in 30001 30002 30003; do
  CODE=HBR-0042; [ $n = 30003 ] && CODE=HBR-9999
  curl -s -o /dev/null -w "%{http_code}\n" -X POST "https://<app>.azurewebsites.net/api/v1/orders" \
    -H "x-functions-key: <key>" -H "Content-Type: application/json" \
    -d "{\"orderNumber\":\"ORD-$n\",\"propertyCode\":\"$CODE\",\"category\":\"Plumbing\",\"amount\":120,\"partnerId\":\"LAB\"}"
done
```

**Checkpoint:** three `202` responses. The API accepts orders quickly and validates the property later, in the queue handler. Wait two or three minutes for telemetry to arrive.

### Lab 18.2 – Invocations and failures

In **Application Insights › Logs**, run the first query from `infra/monitoring/queries.kql` (invocations and failures per function).

**Checkpoint:** `SubmitOrder` shows three successful requests, and `ProcessOrder` shows at least three invocations. The order for `HBR-9999` was dead-lettered as a permanent failure, which the handler treats as *handled*, so it may not count as a failed invocation. That's why the dead-letter metric exists.

### Lab 18.3 – One order, end to end

Find the operation for `ORD-30001`:

```kusto
union requests, dependencies, traces
| where timestamp > ago(1h)
| where customDimensions has "ORD-30001" or message has "ORD-30001"
| project timestamp, itemType, name, message, operation_Id
| order by timestamp asc
```

Copy an `operation_Id` and run the second query from `queries.kql` (everything in one operation) with it.

**Checkpoint:** in order: the `SubmitOrder` request, the Service Bus send, the `ProcessOrder` invocation, its Dataverse and LedgerOne dependencies, and the log lines in between, all sharing one operation ID. That's correlation across an HTTP call and a queue, without any code of your own beyond the middleware and spans from this chapter.

### Lab 18.4 – The dead-lettered order

Run the dead-letter query (number 6 in `queries.kql`).

**Checkpoint:** one dead-lettered message on `order-intake` in the last hour. Then find the order's own story with the query from Lab 18.3, using `ORD-30003`: the handler's dead-letter log entry for the order, with the exception message *Property HBR-9999 does not exist.*

### Lab 18.5 – One invocation's logs

Pick any `ProcessOrder` trace from Lab 18.3, copy its `InvocationId` custom dimension, and run query 4 (all logs written during one invocation).

**Checkpoint:** only that invocation's log lines, even though many invocations ran on the same instance at the same time. The middleware's logging scope added the `InvocationId` to every line.

### Lab 18.6 – Create an alert

Create a log search alert rule on the Application Insights resource with query 6, aggregated as the sum of `dead_lettered` over the last hour, firing when it's greater than 0, evaluated every 15 minutes, with an action group that emails you.

**Checkpoint:** within the evaluation period after Lab 18.1, you receive the alert email. Then disable or delete the rule if you don't want lab alerts; in production, point it at the on-call channel and link the runbook.

### Lab 18.7 – Check your ingestion

In the Log Analytics workspace, run:

```kusto
Usage
| where TimeGenerated > ago(7d)
| summarize GB = sum(Quantity) / 1024 by DataType
| order by GB desc
```

**Checkpoint:** you can see which tables dominate ingestion. In most apps, traces and dependencies top the list; that's where log-level and sampling decisions pay off.

## Hands-on exercise

::: exercise
**Goal:** answer "what happened to lease LSE-0042?" in one query.

**Steps:** add `LeaseNumber` as a tag on a span in each lease activity, then write a KQL query that finds every operation tagged with a given lease number and lists its steps in order.

**Done when:** for one test lease, the query shows the webhook, the queue message, the orchestration's activities and the callback.

**Stretch:** turn the query into a workbook parameterised by lease number.
:::

## Knowledge check

::: knowledge-check
1. Which two parts of a Function App emit telemetry, and how do you switch each to OpenTelemetry?
2. What joins a request, its Dataverse calls and its logs in Application Insights?
3. Why use `LoggerMessage` instead of string interpolation?
4. What does disabling local authentication on Application Insights protect against?
5. Name four alerts every integration app should have.
:::

Answers are in Appendix G.

::: summary
- OpenTelemetry is configured in the host (`telemetryMode`) and the worker (`UseFunctionsWorkerDefaults` plus an exporter).
- One trace ID ties functions, Dataverse and partner calls, logs and messages together.
- Structured, scoped, source-generated logs make every event queryable by business key.
- KQL queries, a workbook and four alerts give the team eyes on every integration.
- Logs never contain secrets; telemetry is sent with Entra authentication.
:::

## References

- [Use OpenTelemetry with Azure Functions](https://learn.microsoft.com/azure/azure-functions/opentelemetry-howto) – Microsoft Learn, checked 2026-10-27.
- [Monitor executions in Azure Functions](https://learn.microsoft.com/azure/azure-functions/functions-monitoring) – Microsoft Learn, checked 2026-10-27.
- [Compile-time logging source generation](https://learn.microsoft.com/dotnet/core/extensions/logger-message-generator) – Microsoft Learn, checked 2026-10-27.
- [Microsoft Entra authentication for Application Insights](https://learn.microsoft.com/azure/azure-monitor/app/azure-ad-authentication) – Microsoft Learn, checked 2026-10-27.
- [Azure Monitor workbooks](https://learn.microsoft.com/azure/azure-monitor/visualize/workbooks-overview) – Microsoft Learn, checked 2026-10-27.
- [Monitoring Azure Service Bus data reference](https://learn.microsoft.com/azure/service-bus-messaging/monitor-service-bus-reference) – Microsoft Learn, checked 2026-10-27.

<!-- Glossary terms introduced in this chapter: OpenTelemetry, trace, span, metric, trace ID, operation ID, structured log, scope, KQL, workbook, action group -->

<!-- expanded: deep dive + guided lab -->
