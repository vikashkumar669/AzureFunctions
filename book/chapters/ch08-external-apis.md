---
chapter: 8
part: "Part III — Real-World Integration Patterns"
title: "Integrating External REST APIs"
build-summary: "Harbourline Connect calls two partner systems reliably: LedgerOne with an API key from Key Vault, SignBridge with a Microsoft Entra ID token, both behind timeouts, retries and circuit breakers that you can watch fail and recover against local mocks."
builds:
  - "Typed HTTP clients for LedgerOne and SignBridge"
  - "A standard resilience pipeline, with retries disabled where they would be unsafe"
  - "Idempotency keys that make retried writes safe"
versions: ".NET 10 · Microsoft.Extensions.Http.Resilience 10.11.0 · Azure.Identity 1.21.0"
---

::: objectives
- Use `IHttpClientFactory` and typed clients instead of creating `HttpClient` by hand.
- Authenticate to a third-party API with a key from Key Vault, and to an Entra-protected API with a managed identity token.
- Add timeouts, retries and a circuit breaker with `Microsoft.Extensions.Http.Resilience`, and explain the order in which they apply.
- Decide which requests may be retried, and make writes safe to retry with idempotency keys.
- Test HTTP clients without a network, and exercise failure handling against mocks.
:::

## The business problem

Harbourline's leases are signed electronically with **SignBridge**, and its invoices live in **LedgerOne**, the finance system. Both are fictional partners in this book, but their behaviour is typical. LedgerOne is reliable on most days and slow on the first working day of the month. SignBridge occasionally returns 503 for a few seconds during its deployments.

The old integration called both with `new HttpClient()` inside a loop, with no timeout. On a bad day, threads piled up waiting for LedgerOne, and when the integration finally retried, finance found the same customer created twice.

## How it works

### Typed clients and IHttpClientFactory

`HttpClient` is meant to be long-lived, but a single static instance never notices DNS changes. **IHttpClientFactory** solves both problems: it pools and recycles the underlying handlers, and it lets you configure each client once. A **typed client** is a class that receives a configured `HttpClient` in its constructor and exposes business methods such as `GetCustomerAsync`. Callers depend on its interface and never see HTTP.

### Resilience strategies

Remote calls fail in different ways, and each failure needs its own response (@fig:resilience):

- An **attempt timeout** stops one attempt that hangs.
- **Retry** repeats a failed attempt after a delay, for failures that are likely to pass: HTTP 408, 429, 5xx, and network errors. Delays grow exponentially, with random **jitter** so that many clients don't retry in lock-step.
- A **circuit breaker** stops calling a system that keeps failing. While it is *open*, calls fail at once instead of adding load to a struggling partner; after a pause it lets a trial call through.
- A **total timeout** caps the whole operation, retries included.

`Microsoft.Extensions.Http.Resilience` packages these as the **standard resilience handler**, built on Polly, with sensible defaults that you can override.

![A request passes through nested resilience strategies before it reaches LedgerOne; each one handles a different kind of failure.](figures/ch08/resilience.svg){#fig:resilience}

### When a retry is safe

Retrying a `GET` is harmless. Retrying a `POST` that *did* succeed, but whose response was lost, repeats the side effect: a second customer, a second envelope. An operation is **idempotent** if doing it twice has the same effect as doing it once.

Two tools make writes idempotent. `PUT` to a URL that names the resource (`/customers/C-1001`) is idempotent by design. For other writes, an **idempotency key**, a unique value sent in a header, lets the server recognise a repeat and return the original result. Where a partner offers neither, don't retry the write.

## Architecture

::: {#tbl:components caption="Partner integrations in this chapter"}
| Component | Role | Authenticates with | Failure path |
|---|---|---|---|
| `LedgerOneClient` | Customers, invoices, orders | API key header, from Key Vault | Retries on 408/429/5xx; circuit breaker |
| `SignBridgeClient` | Signature envelopes | Entra ID token for the managed identity | No retry on `POST`; circuit breaker |
| Mocks (`samples/Harbourline.Mocks`) | Local stand-ins with injected failures | Same as the real APIs | Configurable failure rate |
:::

## Prerequisites

- Chapter 5: Key Vault references and managed identities.
- Chapter 6: dependency injection and options.
- The mocks running locally (`dotnet run --project samples/Harbourline.Mocks`).

## Step-by-step implementation

### Step 1 – Model the partner's data

```{.csharp #lst:models include="src/Harbourline.Connect.Core/Integrations/LedgerOneModels.cs" region="ledger-models" caption="LedgerOne's JSON as C# records"}
```

`JsonPropertyName` pins each property to the partner's spelling. Your C# names can then follow your conventions, and a rename in your code can't silently change the wire format.

### Step 2 – Write the typed client

```{.csharp #lst:client include="src/Harbourline.Connect.Core/Integrations/LedgerOneClient.cs" region="ledger-client" caption="A typed client for LedgerOne"}
```

1. For a lookup, 404 is an answer, not an error. The method returns `null`.
2. Every other non-success status throws `HttpRequestException`, which carries the status code. Callers (Chapter 24) use that code to decide whether a failure is transient.
3. The idempotency key travels in a header. Callers choose it; Step 5 explains how.

### Step 3 – Register the client with resilience

```{.csharp #lst:ledgerreg include="src/Harbourline.Connect.Core/Integrations/IntegrationRegistration.cs" region="ledger-registration" caption="LedgerOne: base address, API key and the standard resilience pipeline"}
```

1. A trailing slash on the base address matters: relative URIs like `customers/C-1` are appended to it.
2. The key comes from configuration, which in Azure is a Key Vault reference. It is set once, on the client, and never logged.
3. The standard pipeline adds the strategies in @fig:resilience.
4. Three retries, with exponential backoff and jitter, on top of the first attempt.

::: warning
Keep the circuit breaker's sampling duration at least twice the attempt timeout; the library validates this and refuses to start otherwise. If you raise the attempt timeout, raise the sampling duration too.
:::

### Step 4 – Call an Entra-protected API with a managed identity

SignBridge is protected by Microsoft Entra ID. Instead of a key, Harbourline sends a token for its managed identity, added by a delegating handler (@lst:bearer).

```{.csharp #lst:bearer include="src/Harbourline.Connect.Core/Integrations/BearerTokenHandler.cs" region="bearer-handler" caption="A handler that adds an Entra ID token to every request"}
```

```{.csharp #lst:sbreg include="src/Harbourline.Connect.Core/Integrations/IntegrationRegistration.cs" region="signbridge-registration" caption="SignBridge: a token handler, and no retries for POST"}
```

1. The handler uses the same `TokenCredential` as the Dataverse client: one identity, many resources. SignBridge's administrators grant that identity access in their own app registration.
2. Creating an envelope is not idempotent, and SignBridge offers no idempotency key. Retrying a `POST` could send a tenant two envelopes, so retries are disabled for unsafe methods. Timeouts and the circuit breaker still apply.

### Step 5 – Choose idempotency keys

A key must be the same every time the *same* logical operation is retried, and different for different operations. Harbourline's rules:

- **Customer upsert:** customer ID plus a hash of the fields being written (`C-1001-3FA2…`). The same change retried is recognised; a later, different change is not.
- **Order submission:** the order number, which is unique by definition.
- **A message from Service Bus:** the message ID (Chapter 11), which stays the same across redeliveries.

Never use a new GUID per attempt: that defeats the purpose.

### Step 6 – Watch it work against the mocks

The mocks fail a configurable share of requests with 503 or 429 (@lst:mockfail) and honour idempotency keys (@lst:mockidem).

```{.csharp #lst:mockfail include="samples/Harbourline.Mocks/Program.cs" region="mock-failures" caption="The mock fails some requests on purpose"}
```

1. Requests without the right API key get 401, as the real LedgerOne would answer.
2. A random share of requests fail with a retryable status.

```{.csharp #lst:mockidem include="samples/Harbourline.Mocks/Program.cs" region="mock-idempotency" caption="The mock returns the same receipt for a repeated key"}
```

1. The first request with a key creates the order; repeats return the stored receipt.
2. The response is the same either way, so a retried call can't tell the difference, which is the point.

Start the mocks with `FailureRate=0.3`, run the app, and call an endpoint that uses LedgerOne several times. The logs show failed attempts followed by successes; the caller sees only successes, slightly slower.

## Screens you will use

::: {.capture #fig:retries caption="Application Insights shows the retried dependency calls behind one successful operation." app="Azure portal" path="Application Insights › Transaction search › an operation › End-to-end transaction details" annotations="1 First attempt: 503; 2 Retry after backoff: 200; 3 Total duration"}
:::

## Complete working code

- `src/Harbourline.Connect.Core/Integrations/`: models, both clients, the token handler, HMAC verification (Chapter 12) and registration.
- `samples/Harbourline.Mocks/Program.cs`: the mock partner APIs.
- `tests/.../Integrations/LedgerOneClientTests.cs`: client tests with a stub handler.

## Configuration

::: {#tbl:settings caption="Partner settings"}
| Setting | Local | Azure | Secret? |
|---|---|---|---|
| `LedgerOne__BaseUrl` | `http://localhost:5080/ledgerone/` | Partner's URL | No |
| `LedgerOne__ApiKey` | `local-development-key` (mocks only) | Key Vault reference | Yes |
| `SignBridge__BaseUrl` | `http://localhost:5080/signbridge/` | Partner's URL | No |
| `SignBridge__Scope` | `api://<signbridge-app-id>/.default` | Same | No |
:::

Both options classes are validated at start-up, like `DataverseOptions`.

## Sample input and output

```text
PUT /ledgerone/customers/C-1001
x-api-key: (from Key Vault)
Idempotency-Key: C-1001-3FA2B1C09D4E5F60
Content-Type: application/json

{"customerId":"C-1001","name":"Northwind Lettings (fictional)",
 "email":"accounts@northwind.example","phone":null,"city":"Leeds",
 "modifiedUtc":"2026-10-27T09:30:00+00:00"}

HTTP/1.1 200 OK
```

## Testing

HTTP clients are tested with a stub `HttpMessageHandler` that records the request and returns a canned response, so no network is involved. `LedgerOneClientTests` proves that 404 becomes `null`, that 503 throws with the status code, and that upserts send `PUT` to the right URL with the idempotency key. Resilience behaviour is the library's responsibility; test your configuration against the mocks, as in Step 6.

## Common errors and troubleshooting

::: {#tbl:errors caption="Errors you may see in this chapter"}
| Symptom | Likely cause | Fix |
|---|---|---|
| `InvalidOperationException` about sampling duration at start-up | Circuit breaker sampling < 2 × attempt timeout | Raise the sampling duration |
| Relative URL calls the wrong path | Base address without trailing slash | End `BaseUrl` with `/` |
| `BrokenCircuitException` | Circuit open after repeated failures | Expected; the partner is down. Treat as transient |
| `TimeoutRejectedException` | Attempt or total timeout reached | Check the partner's latency; adjust timeouts deliberately |
| Duplicate customers in LedgerOne | Retried write without idempotency | Use `PUT` or an idempotency key; disable retry otherwise |
| 401 from SignBridge | Identity not granted on SignBridge's app registration | Ask the partner to grant your identity's app ID |
:::

## Security considerations

::: security
Never log request headers wholesale: they carry API keys and bearer tokens. Log the method, URL path, status code and duration instead; Application Insights' dependency tracking already does exactly that. Validate the partner's TLS certificate (the default); never disable certificate validation, even "temporarily" in development.
:::

## Performance and scalability

::: performance
Retries multiply load: three retries can quadruple the calls a failing partner receives. The circuit breaker and jitter protect the partner; the total timeout protects you. Remember the HTTP-trigger limit of 230 seconds when you set timeouts for calls made inside HTTP functions.
:::

## Alternatives and design decisions

You could write retry loops by hand. Harbourline doesn't: hand-written loops usually lack jitter, retry the wrong status codes, and ignore cancellation. A second alternative is to call partners only from queued work (Chapters 11 and 24) and never from HTTP functions. Harbourline does that for writes; for the occasional read in an HTTP request, a resilient client with tight timeouts is enough.

## Best practices

- Register every partner as a typed client; never `new HttpClient()` per call.
- Add the standard resilience handler and set timeouts deliberately.
- Retry only idempotent operations; disable retries for unsafe methods otherwise.
- Choose idempotency keys that are stable across retries of the same operation.
- Keep keys in Key Vault and prefer Entra ID tokens where the partner supports them.
- Develop and test against mocks that fail on purpose.

## Hands-on exercise

::: exercise
**Goal:** make envelope creation safe to retry.

**Steps:** extend the SignBridge mock to accept an `Idempotency-Key` header on `POST /envelopes`. Send the lease number as the key from `SignBridgeClient`, and re-enable retries for that client.

**Done when:** with `FailureRate=0.5`, creating an envelope for one lease ten times produces one envelope.

**Stretch:** add a hedging strategy (`AddStandardHedgingHandler`) to the LedgerOne read client and compare tail latency in Application Insights.
:::

## Knowledge check

::: knowledge-check
1. What two problems does `IHttpClientFactory` solve?
2. Name the four strategies in the standard resilience handler in the order a request meets them.
3. Why is retrying a `POST` dangerous, and what makes it safe?
4. Why is a new GUID per attempt a bad idempotency key?
5. How does `SignBridgeClient` authenticate, and what secret does Harbourline store for it?
:::

Answers are in Appendix G.

::: summary
- Typed clients from `IHttpClientFactory` give each partner one configured, reusable client.
- The standard resilience handler adds timeouts, retries with jitter and a circuit breaker.
- Only idempotent operations may be retried; `PUT` and idempotency keys make writes idempotent.
- API keys live in Key Vault; Entra-protected partners get managed identity tokens instead.
- Mocks that fail on purpose make failure handling visible before production does.
:::

## References

- [Use IHttpClientFactory to implement resilient HTTP requests](https://learn.microsoft.com/dotnet/architecture/microservices/implement-resilient-applications/use-httpclientfactory-to-implement-resilient-http-requests) – Microsoft Learn, checked 2026-10-27.
- [Build resilient HTTP apps: key development patterns](https://learn.microsoft.com/dotnet/core/resilience/http-resilience) – Microsoft Learn, checked 2026-10-27.
- [Transient fault handling](https://learn.microsoft.com/azure/architecture/best-practices/transient-faults) – Microsoft Learn, checked 2026-10-27.
- [Circuit Breaker pattern](https://learn.microsoft.com/azure/architecture/patterns/circuit-breaker) – Microsoft Learn, checked 2026-10-27.
- [Manage connections in Azure Functions](https://learn.microsoft.com/azure/azure-functions/manage-connections) – Microsoft Learn, checked 2026-10-27.

<!-- Glossary terms introduced in this chapter: typed client, IHttpClientFactory, retry, jitter, circuit breaker, attempt timeout, idempotent, idempotency key -->
