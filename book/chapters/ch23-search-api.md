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

::: note
The caching policies need a cache to store responses in. Most API Management tiers include a built-in cache, but the **Consumption** tier used in Chapter 14 doesn't: there you must first configure an external Redis-compatible cache, such as Azure Managed Redis, for the API Management instance. Without one, these policies have nowhere to store responses. Weigh that extra resource against the in-memory cache of Step 2, which costs nothing.
:::

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

## Deep dive: a consistency budget for cached reads

Every cache trades freshness for speed. The question is never "should we cache?" but "how stale may this answer be, and who has agreed to that?" Harbourline calls the agreed staleness a **consistency budget**, and writes it next to each cached endpoint.

### Where the staleness comes from

A search response can be stale for several reasons that add up:

::: {#tbl:staleness caption="Sources of staleness for one search response"}
| Layer | Holds a response for | Shared by |
|---|---|---|
| In-memory cache in the Function App | Up to 60 s (`PropertySearch.CacheFor`) | Requests on the **same instance** only |
| API Management cache (if configured) | Up to 60 s (`cache-store duration`) | All callers of the gateway |
| `Cache-Control: public, max-age=60` | Up to 60 s | Browsers and proxies between the partner and you |
:::

In the worst case these can stack, so a change in Dataverse might take a few minutes to reach a partner's screen. For property search, Harbourline's lettings team agreed that "within five minutes" is fine: a newly let flat appearing for a few more minutes costs an email, while a slow search costs partners. For the order status endpoint (Chapter 24), the budget is zero, and nothing is cached.

### Per-instance caches disagree

The in-memory cache lives in each Function App instance. With five instances, five caches fill and expire independently, so two requests a second apart can return different versions of the same page. For search that's acceptable; for anything where users compare results ("I just saw it, now it's gone") it may not be. When consistency across instances matters, use a shared cache (Redis) or the gateway cache, which all instances sit behind.

### Only cache what doesn't depend on the caller

A shared cache returns one caller's response to another caller. That's safe only when the response depends on nothing but the cache key: here, the query parameters. Never cache, in a shared layer, responses that depend on who's asking (the lease pack in Chapter 22) or that contain personal data. The in-memory cache in `PropertySearch` builds its key from city, bedrooms and size only, and caches **only first pages**: cursor pages are rarely repeated, so caching them would fill memory for little gain.

### Keyset paging and stable results

The search pages by keyset: each page asks for rows after the last `(name, id)` pair of the previous page. Unlike page numbers, a keyset cursor never skips or repeats rows when rows are added or removed between requests, and every page costs the same, however deep. The trade-off is that a client can't jump to "page 7"; it can only go forward. For a partner API that feeds listings, that's the right trade.

### Measure the cache's value

A cache that's never hit only adds complexity. The two numbers that justify it are the **hit ratio** (from API Management analytics, or a counter around `GetOrCreateAsync`) and the **reduction in Dataverse calls** during a load test with the cache on and off (Step 5). If the hit ratio is low, the keys are too specific (for example, including `size` values that every client sets differently); standardize the parameters before adding more caching.

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

## Guided lab: page, cache and observe

**Requires:** the host running locally and the three sample properties in Leeds (Chapter 4's `--sample-data`).

### Lab 23.1 – Page with a cursor

Ask for one property per page:

```bash
curl -s "http://localhost:7071/api/v1/search/properties?city=Leeds&size=1" | jq
```

**Checkpoint:** one item (the first by name) and a `continuationToken`. Pass the token as `after` to get the next page, and repeat until no token comes back:

```bash
AFTER=$(curl -s "http://localhost:7071/api/v1/search/properties?city=Leeds&size=1" | jq -r .continuationToken)
curl -s -G "http://localhost:7071/api/v1/search/properties" --data-urlencode "city=Leeds" \
  --data-urlencode "size=1" --data-urlencode "after=$AFTER" | jq '.items[].name'
```

You see each sample property exactly once, in name order.

### Lab 23.2 – A tampered cursor

Change a character in `$AFTER` and call again. **Checkpoint:** `400` with the title *Invalid cursor*.

### Lab 23.3 – See the cache at work

1. Call the first page (`city=Leeds&size=1`) and note the rent of the first property.
2. In Dynamics 365, change that property's monthly rent.
3. Call the first page again immediately.

**Checkpoint:** the old rent is still returned. Wait just over 60 seconds and call again: the new rent appears. You've just measured the in-memory cache's consistency budget.

Now ask for the **second** page with the cursor (Lab 23.1) right after changing the second property's rent. **Checkpoint:** the new value appears at once, because cursor pages aren't cached.

### Lab 23.4 – Check the response headers

```bash
curl -s -D - -o /dev/null "http://localhost:7071/api/v1/search/properties?city=Leeds&size=1"
```

**Checkpoint:** `Cache-Control: public, max-age=60`. Any HTTP cache between the caller and you may now keep the response for a minute; that's part of the consistency budget too.

### Lab 23.5 – Count the Dataverse calls

Run the first-page request 20 times in a row:

```bash
for i in $(seq 20); do curl -s -o /dev/null "http://localhost:7071/api/v1/search/properties?city=Leeds&size=1"; done
```

**Checkpoint:** in Application Insights (or the debug output), there's about one Dataverse query for these 20 requests, as long as they run on one instance within the minute. Repeat with `size=2` and then `size=3`: each new `size` is a new cache key and a new query. That's why API clients should use a standard page size.

### Lab 23.6 – Write the budget down

Add a line to the API's documentation (the OpenAPI description in `infra/apim/partner-api.openapi.yaml` has room for a description on the operation) saying how stale search results may be. **Checkpoint:** a partner reading the documentation would not be surprised by Lab 23.3.

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
- [Caching overview for API Management](https://learn.microsoft.com/azure/api-management/caching-overview) – Microsoft Learn, checked 2026-11-05.
- [What is Azure Load Testing?](https://learn.microsoft.com/azure/load-testing/overview-what-is-azure-load-testing) – Microsoft Learn, checked 2026-10-27.
- [Caching in .NET](https://learn.microsoft.com/dotnet/core/extensions/caching) – Microsoft Learn, checked 2026-10-27.

<!-- Glossary terms introduced in this chapter: keyset paging, cursor, response cache, load test, p95 -->

<!-- expanded: deep dive + guided lab -->
