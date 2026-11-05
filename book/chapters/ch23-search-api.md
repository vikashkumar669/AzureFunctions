---
chapter: 23
part: "Part V — Capstone Projects"
title: "Project 3 — High-Volume Property Search API"
build-summary: "Harbourline's public property search answers quickly at any depth of results, serves popular searches from cache, and has a load test that tells the team where its limits are before customers find them."
builds:
  - "Keyset paging that costs the same on page 1 and page 500"
  - "Two cache layers: in the function and in API Management"
  - "A load-test plan with targets, a ramp and pass criteria"
versions: ".NET 10 · Dataverse.Client 1.2.27 · Microsoft.Extensions.Caching.Memory 10.0.14 · Azure API Management · Azure Load Testing"
---

::: objectives
- Explain why page-number paging slows down with depth, and implement keyset paging.
- Encode a cursor safely and keep sorting stable.
- Cache read results in the function and at the gateway, and choose expiry times.
- Plan and run a load test with explicit targets.
- Read the results and decide which limit to raise or protect.
:::

## The business problem

Harbourline is listing available properties on partner portals and its own website. Search traffic is spiky: a newsletter can bring thousands of searches in minutes, most of them identical ("Leeds, two bedrooms"). Every search that reaches Dataverse spends part of the integration user's service protection budget (Chapter 15), which the order and sync jobs also need.

The goals: fast responses, a stable cost per request however deep users scroll, and as few Dataverse calls as possible.

## How it works

### Keyset paging

Chapter 7's API pages with Dataverse's paging cookie. That is correct, but each page request still repeats the query, and clients must follow pages in order. **Keyset paging** (or *seek* paging) asks instead: "the next 20 rows *after* this one, in this order". With a sort on name and ID, the condition for the next page is:

> name > last name, **or** (name = last name **and** id > last id)

With an index on the sort columns, the database seeks straight to the position, so page 500 costs the same as page 1. The **cursor** sent to the client contains just the last row's name and ID. Requesting one row more than the page size tells you whether there is a next page without counting.

### Caching

Most searches repeat. Two caches remove them from Dataverse:

- An **in-memory cache** in each function instance keeps first pages for 60 seconds. It's free and fast, but each instance has its own copy.
- **API Management's cache** stores whole responses at the gateway, shared by all callers, keyed by URL and chosen query parameters. Requests served from it never reach the Function App.

The function also sends `Cache-Control: public, max-age=60`, so browsers and other HTTP caches may reuse responses too.

Sixty seconds is a business decision: a property let a minute ago might still appear in search. Harbourline accepted that; the booking step checks live data.

## Architecture

::: {#tbl:components caption="Search components"}
| Component | Role | Authenticates with | Failure path |
|---|---|---|---|
| API Management | Response cache, rate limit | Subscription key + token (Chapter 14) | 429 when over limit |
| `SearchProperties` | Parses, sets cache headers | APIM's identity via Easy Auth | 400 for a bad cursor |
| `PropertySearch` | Keyset query, memory cache | Managed identity | — |
| Dataverse | Source | Application user | Throttling (Chapter 15) |
:::

## Prerequisites

Chapters 7, 14 and 15. An alternate key or index on `hl_name` is not required; Dataverse indexes primary name columns.

## Step-by-step implementation

### Step 1 – Query by keyset

```{.csharp #lst:keyset include="src/Harbourline.Connect.Core/Search/PropertySearch.cs" region="keyset" caption="Keyset paging: seek past the last row"}
```

1. One row more than the page size reveals whether another page exists.
2. The seek condition, as an OR of two branches.
3. The cursor is made from the last row actually returned.

The cursor is Base64Url-encoded JSON, opaque to clients and validated on the way back in (a malformed cursor is a 400).

### Step 2 – Cache first pages

```{.csharp #lst:cache include="src/Harbourline.Connect.Core/Search/PropertySearch.cs" region="search-cache" caption="Caching popular first pages for 60 seconds"}
```

1. The key normalises the inputs, so "Leeds" and "leeds" share an entry.
2. Absolute expiry: an entry never lives longer than 60 seconds.

Only first pages are cached: they get most traffic, and deep pages are cheap anyway with keyset paging.

### Step 3 – The function

```{.csharp #lst:searchfn include="src/Harbourline.Connect.Functions/Search/PropertySearchFunction.cs" region="search-function" caption="GET /api/v1/search/properties"}
```

1. The cache header lets intermediaries reuse the response for 60 seconds.

### Step 4 – Cache at the gateway

Add to the search operation's policy in API Management:

```xml
<inbound>
  <base />
  <cache-lookup vary-by-developer="false"
                vary-by-developer-groups="false">
    <vary-by-query-parameter>city</vary-by-query-parameter>
    <vary-by-query-parameter>minBedrooms</vary-by-query-parameter>
    <vary-by-query-parameter>after</vary-by-query-parameter>
    <vary-by-query-parameter>size</vary-by-query-parameter>
  </cache-lookup>
</inbound>
<outbound>
  <base />
  <cache-store duration="60" />
</outbound>
```

The response depends only on these parameters, never on who asks, which is what makes a shared cache safe here. Never cache responses that depend on the caller's identity this way.

### Step 5 – Load-test with a plan

A load test answers a question; write the question first. Harbourline's plan:

::: {#tbl:loadplan caption="Search load-test plan"}
| Item | Value |
|---|---|
| Question | Can search serve the newsletter peak without throttling the integration user? |
| Target | 50 requests/second for 10 minutes, p95 below 800 ms, error rate below 1% |
| Mix | 70% three popular first pages, 20% random cities, 10% deep pages via cursors |
| Ramp | 5 → 50 requests/second over 5 minutes, hold 10 minutes |
| Environment | Test: APIM, Function App and a sandbox with production-like data |
| Watch | p95 latency, errors, Dataverse dependency calls, throttling, instance count |
| Pass | Targets met *and* no throttling faults |
:::

Run it with **Azure Load Testing** (JMeter or Locust scripts) against the APIM URL, with test subscription keys. Run once with APIM caching off and once with it on; the difference in Dataverse calls is the value of the cache, measured.

As in Chapter 15, this book publishes no results: they would describe the author's environment, not yours. Record yours in the plan's table, with the date and versions.

## Screens you will use

::: {.capture #fig:loadtest caption="An Azure Load Testing run against the search API." app="Azure portal" path="Azure Load Testing › Tests › property-search › a run" annotations="1 Requests per second; 2 p95 response time; 3 Errors; 4 Server-side metrics from Application Insights"}
:::

## Complete working code

`src/Harbourline.Connect.Core/Search/PropertySearch.cs`, `src/Harbourline.Connect.Functions/Search/PropertySearchFunction.cs`, `tests/.../Search/PropertySearchTests.cs`.

## Configuration

Cache duration (60 seconds) is a constant in `PropertySearch` and in the APIM policy; change both together. Page size is clamped to 1–100.

## Sample input and output

```text
GET /partner/v1/search/properties?city=Leeds&minBedrooms=2&size=2

{"items":[{"code":"HBR-0042","name":"12 Canal Wharf",…},
          {"code":"HBR-0101","name":"3 Mill Lane",…}],
 "continuationToken":"eyJOYW1lIjoiMyBNaWxsIExhbmUiLCJJZCI6Ij…"}
```

(The page's `continuationToken` is the keyset cursor; pass it as `after`.)

## Testing

```{.csharp #lst:keysettest include="tests/Harbourline.Connect.UnitTests/Search/PropertySearchTests.cs" region="test-keyset" caption="The next page seeks past the cursor"}
```

1. One extra row is requested.
2. The seek is an OR filter, as designed.

A second test calls the same first-page search twice with different letter case and checks that Dataverse was queried once.

## Common errors and troubleshooting

::: {#tbl:errors caption="Errors you may see with search"}
| Symptom | Likely cause | Fix |
|---|---|---|
| Rows repeated or skipped across pages | Sort not unique | Always include the ID as last sort key and in the cursor |
| Stale results for longer than expected | Several cache layers adding up | Remember: APIM, function and browser caches each up to 60 s |
| Cache hit rate low | Cache key varies by unimportant parameters | Vary only by parameters that change the result |
| Throttling during the load test | Cache disabled or too many cold misses | Cache at the gateway; rate-limit callers |
:::

## Security considerations

::: security
Public search should expose only public fields. `PropertyDto` was designed for the portal; if it ever gains internal fields, give search its own contract. Shared caches must never store responses that differ by caller.
:::

## Performance and scalability

::: performance
The cheapest Dataverse call is the one you don't make: gateway caching, then in-memory caching, then keyset queries. Protect the integration user's budget for jobs that can't be cached, and consider a separate application user for public read traffic so a search spike can't throttle order processing.
:::

## Alternatives and design decisions

For full-text search, facets and relevance ranking, **Dataverse search** (for in-app use) or a dedicated search index such as Azure AI Search, fed by change tracking, is the right tool. Harbourline's needs are filters and sorting, which Dataverse queries handle; it will revisit the decision if the website asks for "search by description".

## Best practices

- Page deep result sets by keyset, with a unique sort.
- Cache what is identical for every caller, at the gateway first.
- Set cache lifetimes as a business decision, and document them.
- Write the load-test question and pass criteria before running it.
- Keep public contracts minimal.

## Hands-on exercise

::: exercise
**Goal:** add sorting by rent. Extend the cursor with the last rent, adapt the seek condition, and prove with a test that page boundaries never repeat a property with the same rent.
:::

## Knowledge check

::: knowledge-check
1. Why does keyset paging cost the same at any depth?
2. Why is one extra row requested?
3. Which responses may be cached at the gateway, and which must not?
4. What makes a load test's result meaningful?
:::

Answers are in Appendix G.

::: summary
- Keyset paging seeks past the last row; a unique sort keeps it correct.
- Gateway and in-memory caches absorb repeated searches; lifetimes are business decisions.
- A load test starts with a question, targets and pass criteria, and its results belong to your environment.
:::

## References

- [Page results using FetchXml and QueryExpression](https://learn.microsoft.com/power-apps/developer/data-platform/fetchxml/page-results) – Microsoft Learn, checked 2026-10-27.
- [Caching policies in API Management](https://learn.microsoft.com/azure/api-management/api-management-caching-policies) – Microsoft Learn, checked 2026-10-27.
- [What is Azure Load Testing?](https://learn.microsoft.com/azure/load-testing/overview-what-is-azure-load-testing) – Microsoft Learn, checked 2026-10-27.
- [Caching in .NET](https://learn.microsoft.com/dotnet/core/extensions/caching) – Microsoft Learn, checked 2026-10-27.

<!-- Glossary terms introduced in this chapter: keyset paging, cursor, response cache, load test, p95 -->
