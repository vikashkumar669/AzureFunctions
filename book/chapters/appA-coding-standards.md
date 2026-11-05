---
chapter: "A"
appendix: true
title: "C# Coding Standards for Integration Code"
---

These are the rules the Harbourline team applies in code review. Most are enforced by the build (`Directory.Build.props`, `.editorconfig`); the rest are judgement.

## Project structure

- Business logic lives in `Harbourline.Connect.Core`, which never references Azure Functions packages.
- Function classes are thin: parse the trigger input, call one service, map the result. No Dataverse queries in functions.
- Contracts (request and response types) live in their own project and never expose Dataverse types.
- One registration extension method per area (`AddHarbourlineCore`, `AddHarbourlineIntegrations`, `AddHarbourlineFunctions`).

## Language and compiler

- Nullable reference types on; treat warnings as errors; analysis level `latest`.
- `sealed` by default; `record` for data; primary constructors for dependency injection.
- `async` all the way; pass `CancellationToken` everywhere it is offered; `ConfigureAwait(false)` in Core.
- No `DateTime.UtcNow` in logic: inject `TimeProvider`.
- No string literals for Dataverse names: use the constants in `Schema.cs`.
- Lines of 72 characters or fewer (80 at most in printed listings).

## Dataverse

- Register one `ServiceClient` per app; depend on `IOrganizationServiceAsync2`.
- Always name the columns you retrieve; never `ColumnSet(true)`.
- Identify rows by alternate key; write with upsert where repeats are possible.
- Page every query that can exceed one page; sort by a unique key last.
- Catch Dataverse faults only to map *expected* outcomes (not found, duplicate, version mismatch); let the rest throw.
- Use bulk messages for more than a few hundred rows; bound parallelism.

## HTTP and messaging

- Typed clients from `IHttpClientFactory` with the standard resilience handler.
- Retry only idempotent operations; send idempotency keys on writes.
- Errors as RFC 9457 problem details; never return exception messages from Dataverse or partners.
- Settle Service Bus messages explicitly when you need to dead-letter; classify failures as transient or permanent.

## Logging

- Source-generated `[LoggerMessage]` methods; message templates with named properties.
- Business keys as properties; never secrets, tokens, request bodies or personal data beyond IDs.
- One log event per step, not per row.

## Security

- No secrets in code, settings or the repository; managed identities for Azure and Dataverse; Key Vault references for third-party secrets.
- Validate every input, including from "trusted" callers and files.
- `AuthorizationLevel.Anonymous` only with authentication in code (signatures) or for endpoints that reveal nothing.

## Tests

- Every service has unit tests; every documented error code it handles has a test.
- Tests use fictional data and `TestContext.Current.CancellationToken`.
- Assert on outcomes and requests, not on internal call sequences.
