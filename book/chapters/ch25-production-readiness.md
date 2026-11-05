---
chapter: 25
part: "Part V — Capstone Projects"
title: "Production Readiness and Final Architecture"
build-summary: "Harbourline Connect goes live with a reviewed architecture, a production-readiness checklist, recovery plans, runbooks for the alerts it raises, and a cost model the business understands."
builds:
  - "The final Harbourline reference architecture"
  - "A production-readiness checklist across security, reliability, performance, operations and cost"
  - "Recovery objectives, backup notes and runbooks"
versions: "Everything in this book, as built in Chapters 1–24"
---

::: objectives
- Review an Azure Functions and Dataverse integration against a structured checklist.
- Set recovery objectives and know how each component is restored.
- Write runbooks that turn alerts into actions.
- Explain what drives the cost of the solution, and how to keep it predictable.
- Decide what to do next after go-live.
:::

## The business problem

Everything works in test. The steering group's question for the go/no-go meeting is different: "What happens when something goes wrong at 2 a.m., who knows what to do, and what will this cost us?" This chapter answers it, and gathers the decisions of the whole book into one architecture.

## How it works

Production readiness is a review, not a feature. Microsoft's **Azure Well-Architected Framework** organises it in five pillars: reliability, security, cost optimization, operational excellence and performance efficiency. Harbourline's checklist (@tbl:checklist) follows them, with one question per line and the chapter where the answer was built.

## Architecture

![Harbourline's final architecture: one Function App behind API Management, connected to Dataverse and partners through identities, queues and events, and observed end to end.](figures/ch25/reference.svg){#fig:reference}

::: {#tbl:architecture caption="The final architecture in one table"}
| Concern | Decision | Chapter |
|---|---|---|
| Hosting | One Function App on Flex Consumption, 2,048 MB, max instances set by capacity check | 2, 20 |
| Identity | System-assigned managed identity; application user with a custom role; no secrets for Microsoft services | 5 |
| APIs | Versioned REST, problem details, ETags; partners through APIM with Entra tokens | 7, 14 |
| Events in | Webhooks (async), Service Bus endpoints, Event Grid blob events, timers | 9, 10, 11, 13 |
| Events out | Typed, resilient HTTP clients with idempotency keys | 8 |
| Long processes | Durable Functions on the Durable Task Scheduler | 12 |
| Volume | Bulk messages, bounded parallelism, split-on-failure, measured | 15 |
| Data quality | Shared validators, reports, reconciliation | 13, 16, 17 |
| Observability | OpenTelemetry to Application Insights, KQL, workbook, alerts | 18 |
| Delivery | Bicep, OIDC pipelines, rolling updates, solutions for Dataverse | 20 |
:::

## Prerequisites

All of Parts I–IV deployed to test; the capstones of Chapters 21–24 working end to end.

## Step-by-step implementation

### Step 1 – Run the readiness checklist

::: {#tbl:checklist caption="Production-readiness checklist"}
| Pillar | Question | Where answered |
|---|---|---|
| Security | Does any Microsoft-service call use a secret? (Expected: no) | 5, 20 |
| Security | Does each identity have only the privileges its code uses? | 5, 22 |
| Security | Is every external endpoint behind APIM or authenticated in code? | 9, 12, 14 |
| Security | Are storage shared keys and App Insights local auth disabled? | 18, 20 |
| Security | Are the remaining secrets in Key Vault, with an owner and a rotation date? | 5, 8 |
| Reliability | Is every write idempotent (alternate keys, idempotency keys)? | 8, 10, 24 |
| Reliability | Does every queue have a dead-letter alert and a replay path? | 11, 24 |
| Reliability | Do timers save checkpoints only on success? | 10, 16 |
| Reliability | Is a partner outage absorbed by queues and circuit breakers? | 8, 11 |
| Performance | Has max instances × concurrency been checked against Dataverse limits? | 2, 15 |
| Performance | Were bulk paths measured in a production-like sandbox, and recorded? | 15, 23 |
| Operations | Does each alert link to a runbook, and has each fired once in test? | 18, this chapter |
| Operations | Can one KQL query trace a lease, order or customer end to end? | 18 |
| Operations | Is every resource created by Bicep, and every release by the pipeline? | 20 |
| Cost | Are max instances, log levels, retention and APIM tier deliberate? | 18, 20, this chapter |
:::

Every "no" becomes either a fix before go-live or a recorded, accepted risk with an owner.

### Step 2 – Set recovery objectives

The business sets two numbers per capability: the **recovery time objective (RTO)**, how long it may be unavailable, and the **recovery point objective (RPO)**, how much data it may lose. @tbl:recovery shows how Harbourline meets them.

::: {#tbl:recovery caption="Recovery approach per component"}
| Component | What protects it | How to recover |
|---|---|---|
| Dataverse | Platform system backups; on-demand backups before releases | Restore the environment from the admin center (Microsoft's backup and restore documentation applies) |
| Function App, APIM, storage, Service Bus, Key Vault | Bicep templates in Git | Redeploy to the same or another region |
| Code | Git tags and pipeline artifacts | Redeploy the previous artifact |
| Messages in flight | Service Bus durability; dead-letter queues | Replay; partners' resends are de-duplicated |
| Checkpoints | Stored in Dataverse | Restored with Dataverse; jobs re-read with overlap |
| Secrets | Key Vault soft delete and purge protection | Recover deleted secrets; partners reissue if needed |
| Workflows | Durable Task Scheduler state | Long workflows resume; re-send stuck ones from Dynamics 365 |
:::

Two practices matter more than any product feature. Take an on-demand Dataverse backup before every release that changes schema. And rehearse: once a quarter, deploy the whole stack from Bicep into an empty resource group and run the smoke tests.

### Step 3 – Write the runbooks

A **runbook** tells the person on call what to check, in order, for one alert. Keep each to one page, in the repository next to the alert definitions. Harbourline's four:

**Function failures above threshold**

1. Open the workbook; which function, since when?
2. Run the health and trace queries (Chapter 18) for one failed operation.
3. Match the exception to the root-cause table in Chapter 18.
4. If it started with a release, redeploy the previous artifact; if with a partner, check the partner's status; if with Dataverse, check the admin center's service health.

**Dead-lettered messages**

1. `dlq-peek` the queue; group by reason.
2. Use the triage guide (Chapter 24) to decide: fix and replay, or reject and inform the partner.
3. Record what happened in the incident log.

**Dataverse throttling**

1. Which function, which user? Bulk loads and syncs share the application user.
2. Reduce parallelism or max instances by setting, without redeploying.
3. If sustained, plan bigger batches or a separate application user for the noisy workload (Chapter 23).

**Scheduled job missing**

1. Is the app running? Are timers disabled by setting?
2. Check the last invocation and its exception.
3. Run the job manually through the admin endpoint (Chapter 10); the watermark makes catching up safe.

### Step 4 – Understand the cost drivers

This book doesn't quote prices: they change, and differ by region and agreement. Use the Azure pricing calculator with your own volumes. What it does give you is the list of drivers:

::: {#tbl:cost caption="What drives the cost of Harbourline Connect"}
| Resource | Billed by | Keep it predictable by |
|---|---|---|
| Flex Consumption | Executions and execution time × memory; always-ready instances if configured | Instance size chosen deliberately; maximum instances capped |
| API Management (Consumption) | Calls | Caching; rate limits per partner |
| Service Bus (Standard) | Base charge plus operations | Batching where possible |
| Durable Task Scheduler | SKU (Consumption or Dedicated) | Consumption for light workloads |
| Application Insights / Log Analytics | Data ingested and retained | Log levels, sampling, retention |
| Storage | Capacity and transactions | Lifecycle rules for reports and packs |
| Dataverse | Licences and capacity; API entitlements | Bulk messages; caching reads |
:::

Set a budget with alerts on the resource group from day one. The surprises Harbourline saw in test were all telemetry: a debug log level left on.

### Step 5 – Hold the architecture review

Present @fig:reference, the checklist with its answers, the recovery table and the cost model. Record the decision and open risks in the repository, as Chapter 2 did for hosting. Then turn on the deploy workflow's push trigger, and go live.

## Screens you will use

::: {.capture #fig:advisor caption="Azure Advisor recommendations for the production resource group." app="Azure portal" path="Advisor › Recommendations (filtered to rg-hlconnect-prod)" annotations="1 Reliability; 2 Security; 3 Cost"}
:::

::: {.capture #fig:budget caption="A monthly budget with alerts on the production resource group." app="Azure portal" path="Cost Management › Budgets › hlconnect-prod" annotations="1 Amount; 2 Alert thresholds; 3 Recipients"}
:::

## Complete working code

The whole repository: `src/`, `tests/`, `samples/`, `infra/`, `pipelines/` and `.github/workflows/`. Appendix B lists every setting; Appendix E lists every error the book discusses.

## Configuration

No new settings. Before go-live, compare production app settings with Appendix B line by line.

## Sample input and output

The go-live smoke run, one request per area (illustrative):

```text
GET  /api/health                         200
GET  /api/v1/properties/HBR-0042         200  ETag "4826113"
POST /partner/v1/orders (test partner)   202  → Approved in 4 s
Webhook: lease LSE-TEST-01 → envelope    sent, workflow running
Feed: smoke-test.csv                     1 read, 1 imported
```

## Testing

The readiness review *is* the test: every checklist line answered with evidence (a query, a screenshot, a test run), not with "should be fine".

## Common errors and troubleshooting

Use the runbooks above, Chapter 18's root-cause table and Appendix E.

## Security considerations

::: security
Before go-live, review every identity, role assignment, key and network path once more, and remove what development left behind: test partners, developer access to production, temporary firewall rules. Then schedule the review to repeat.
:::

## Performance and scalability

::: performance
Production will differ from test. Watch the first month closely: Dataverse dependency latency and throttling, queue depths at month end, and instance counts. Adjust settings, not code, wherever the design allows.
:::

## Alternatives and design decisions

As Harbourline grows, three changes are likely: splitting the Function App by domain (partner APIs, back-office jobs) for independent scaling and releases; a dedicated application user for public read traffic; and moving high-volume readings to an analytical store. The architecture allows each without rewriting Core.

## Best practices

- Review against the Well-Architected pillars; answer every line with evidence.
- Agree RTO and RPO with the business, and rehearse recovery.
- Write a runbook for every alert, and fire every alert once in test.
- Know your cost drivers; set budgets and alerts on day one.
- Record decisions and accepted risks in the repository.

## Hands-on exercise

::: exercise
**Goal:** run the review for your own integration. Copy @tbl:checklist, answer each line for a system you work on, and list the three biggest gaps with an owner and a date.
:::

## Knowledge check

::: knowledge-check
1. What are the five pillars of the Well-Architected Framework?
2. What do RTO and RPO mean, and who sets them?
3. How is a Function App recovered in another region?
4. Name three cost drivers of this solution and one control for each.
:::

Answers are in Appendix G.

::: summary
- Production readiness is a structured review with evidence, not a feeling.
- Recovery relies on infrastructure as code, Dataverse backups, durable messaging and rehearsals.
- Every alert has a one-page runbook; every runbook has been tried.
- Costs follow a short list of drivers that settings and budgets keep in check.
- The final architecture is the sum of the decisions recorded in this book.
:::

## References

- [Azure Well-Architected Framework](https://learn.microsoft.com/azure/well-architected/) – Microsoft Learn, checked 2026-10-27.
- [Reliability in Azure Functions](https://learn.microsoft.com/azure/reliability/reliability-functions) – Microsoft Learn, checked 2026-10-27.
- [Back up and restore environments (Power Platform)](https://learn.microsoft.com/power-platform/admin/backup-restore-environments) – Microsoft Learn, checked 2026-10-27.
- [Azure pricing calculator](https://azure.microsoft.com/pricing/calculator/) – Microsoft, checked 2026-10-27.
- [Create and manage budgets](https://learn.microsoft.com/azure/cost-management-billing/costs/tutorial-acm-create-budgets) – Microsoft Learn, checked 2026-10-27.

<!-- Glossary terms introduced in this chapter: Well-Architected Framework, RTO, RPO, runbook, readiness review -->
