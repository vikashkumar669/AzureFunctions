---
chapter: "N"
appendix: true
title: "Harbourline's Architecture Decision Records"
---

Chapter 1 recommends recording each significant decision. These are the decision records (ADRs) the fictional Harbourline team wrote while building Harbourline Connect, in the order they were made. They're short on purpose: context, decision, consequences, and the alternatives considered. Use them as examples of the format, and as a summary of the reasoning spread through the book. Your own context will differ, so your decisions may too.

## ADR-001: Run integrations on Azure Functions, isolated worker, .NET

**Context.** Several integrations need code: guaranteed delivery to LedgerOne, partner file imports, a public API, long-running signing workflows. The team writes C# for Dataverse plug-ins already.

**Decision.** Build integrations as Azure Functions in C# on the isolated worker model, targeting the current long-term-support .NET release (.NET 10), in one Function App per bounded area (Harbourline Connect).

**Consequences.** Event-driven scaling and pay-per-use for spiky workloads; one language for plug-ins and integrations; standard .NET dependency injection and testing. The team must learn Azure operations (identity, monitoring, deployment), covered by training.

**Alternatives.** Power Automate for everything (rejected for volume, testing and source control needs; still used for simple notifications). A container platform (more control, more operational work than the team needs). The in-process model (retired path; isolated is the supported model).

## ADR-002: Host on the Flex Consumption plan

**Context.** Workloads are bursty: quiet nights, busy mornings, monthly invoice peaks. Some functions need virtual network integration later.

**Decision.** Use Flex Consumption with 2,048-MB instances, a maximum instance count per environment, and always-ready instances only for the partner-facing HTTP group in production.

**Consequences.** Scale to zero for back-office functions; per-function scaling isolates a busy queue from the API. No deployment slots: deployments use rolling updates and rollback means redeploying. Blob triggers must use Event Grid.

**Alternatives.** Premium plan (always warm, slots, higher fixed cost). Dedicated App Service plan (predictable cost, manual scaling). Legacy Consumption (no virtual network, and Flex is the recommended serverless plan for new apps).

## ADR-003: No secrets; managed identity everywhere

**Context.** Secrets in configuration have caused incidents elsewhere in the organization: expired client secrets, keys pasted into tickets.

**Decision.** The Function App authenticates to Dataverse, Service Bus, Storage, Key Vault and the Durable Task Scheduler with its managed identity. Pipelines use OIDC federation. Unavoidable partner secrets live in Key Vault, read through Key Vault references.

**Consequences.** Nothing to rotate for internal connections; role assignments become the security boundary and are reviewed. Local development uses developers' own sign-ins, so developers need data roles in development resources.

**Alternatives.** Client secrets in Key Vault for the Dataverse connection (works, but adds rotation and expiry risk).

## ADR-004: Service Bus for Dataverse events that must not be lost

**Context.** Account changes must reach LedgerOne even if LedgerOne or Harbourline Connect is down for hours.

**Decision.** Register a Dataverse Service Bus endpoint posting to a topic (`account-events`), one subscription per consumer, explicit settlement in functions, dead-letter after a bounded number of attempts.

**Consequences.** Durable buffering, back-pressure and replay. Messages are processed at least once, so every handler is idempotent. The Standard tier was chosen; geo-replication would need Premium and was deferred (ADR-010).

**Alternatives.** Webhooks (simpler, but events are lost if the receiver is down beyond one retry; used for low-risk, fast paths). Event Grid (great for fan-out notifications, but Dataverse posts natively to Service Bus and the team wanted queue semantics).

## ADR-005: Durable Functions with the Durable Task Scheduler for workflows

**Context.** Lease signing waits days for a tenant, with reminders and an expiry.

**Decision.** Implement it as a Durable Functions orchestration using the Durable Task Scheduler as the backend, with the lease number as the instance ID.

**Consequences.** Waiting costs nothing; state and history are managed; the dashboard gives operators visibility. Orchestrators must be deterministic and changes need versioning.

**Alternatives.** A status column plus a timer job polling for due reminders (simple, but scattered logic and harder to observe). Power Automate approvals (good for human approvals inside Microsoft 365; less suitable for partner callbacks with signatures).

## ADR-006: API Management in front of partner APIs

**Context.** External partners need a stable, documented API with per-partner limits and revocation.

**Decision.** Publish partner APIs through API Management (Consumption tier initially), requiring a subscription key and an Entra ID token with app roles, applying rate limits, and calling the Function App with API Management's managed identity. Lock the Function App with Easy Auth.

**Consequences.** One place for partner onboarding, limits and analytics. The Consumption tier has no built-in cache and no `rate-limit-by-key`; per-subscription limits suffice, and gateway caching would need an external cache.

**Alternatives.** Function keys given to partners (no identity, hard to revoke per partner). A higher API Management tier (virtual network and built-in cache, at a fixed monthly cost; revisit when partner volume grows).

## ADR-007: Business keys and upserts for every integration write

**Context.** Retries, replays, duplicate events and migrations all risk duplicates.

**Decision.** Every integrated table has an alternate key on a business identifier shared with the other system (property code, invoice number, order number, reading key). Integration writes are upserts by that key.

**Consequences.** Idempotent writes; simple retries and replays; natural joins between systems. Keys must be designed early and activated before data loads.

**Alternatives.** Storing external IDs in a mapping table (extra lookups and a second source of truth).

## ADR-008: Open-source, permissively licensed libraries only

**Context.** The organization avoids libraries that need additional commercial licences for business use.

**Decision.** Use MIT- or similarly licensed libraries: ClosedXML and the Open XML SDK for Excel, CsvHelper for CSV, xUnit v3 and NSubstitute for tests, Microsoft.Extensions.Http.Resilience for resilience.

**Consequences.** No licence management. Some features of commercial libraries aren't available, and large Excel files need the lower-level Open XML SDK.

**Alternatives.** Commercial Excel libraries (richer features; rejected because of the licence requirement).

## ADR-009: OpenTelemetry with Azure Monitor

**Context.** The team needs end-to-end tracing across HTTP, queues and Dataverse, and wants to avoid lock-in to one telemetry SDK.

**Decision.** Set `telemetryMode` to OpenTelemetry in `host.json`, use the OpenTelemetry SDK in the worker with one `ActivitySource` and one `Meter` for Harbourline code, and export to Azure Monitor (Application Insights).

**Consequences.** Standard trace context propagation; business spans and metrics in one place; the option to send telemetry elsewhere later. Log levels and sampling are managed deliberately to control cost.

**Alternatives.** The classic Application Insights SDK (mature, but proprietary APIs).

## ADR-010: Single region, recovery by redeployment

**Context.** The business set recovery objectives of a few hours for integrations, with no message loss once accepted. Dataverse itself is managed by Microsoft.

**Decision.** Run in one region. Keep all infrastructure in Bicep and all deployments in pipelines, so the environment can be recreated in another region. Use geo-redundant storage where available. Revisit Service Bus Premium geo-replication if objectives tighten.

**Consequences.** Lower cost and complexity. A regional outage means degraded service until the region recovers or the environment is rebuilt elsewhere; messages held in an unavailable namespace wait for its recovery.

**Alternatives.** Active-passive in two regions (lower recovery time, roughly double the infrastructure and much more testing).

## ADR-011: Keyset paging for public list endpoints

**Context.** Partners page through thousands of properties; page-number paging became slow and inconsistent when rows changed between requests.

**Decision.** Public search uses keyset paging with an opaque cursor; first pages are cached briefly in memory.

**Consequences.** Constant cost per page and stable results; clients can only move forward. The cursor format is internal and can change without breaking clients, because it's opaque.

**Alternatives.** Page numbers with `$skip`-style paging (simple, but slow at depth and inconsistent under change).

## ADR-012: Illustrative screens in documentation

**Context.** Portal screens change often and differ between tenants, and screenshots can leak identifiers.

**Decision.** Internal documentation and this book show screens as clearly labelled illustrative mock-ups with fictional data and numbered callouts, backed by a capture plan listing what a real capture must show and mask.

**Consequences.** Documentation stays accurate in what it claims (what to look for), never pretends to be a real capture, and can be replaced with real captures from a sanitized tenant when needed.

**Alternatives.** Real screenshots (exact, but they age quickly and must be masked carefully).
