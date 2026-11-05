---
chapter: 7
part: "Part III — Real-World Integration Patterns"
title: "CRUD APIs for Dynamics 365"
build-summary: "The tenant portal gets a complete, versioned Property API: filtered and paged lists, creation with full validation, partial updates protected by ETags, and errors every client can parse."
builds:
  - "GET, POST and PATCH endpoints for /api/v1/properties"
  - "Opaque continuation tokens for server-side paging"
  - "RFC 9457 problem details and optimistic concurrency with If-Match"
versions: ".NET 10 · Functions 4.x isolated worker · Http.AspNetCore 2.1.1 · Dataverse.Client 1.2.27"
---

::: objectives
- Design resource URLs, methods and status codes for a Dataverse-backed API.
- Validate requests before they cost a Dataverse call, and report every error at once.
- Implement filtering and server-side paging with opaque continuation tokens.
- Return failures as RFC 9457 problem details.
- Prevent lost updates with ETags, `If-Match` and Dataverse row versions.
- Version an API so existing clients keep working.
:::

## The business problem

The tenant portal needs more than one property at a time. Lettings agents search by city and status, scroll through hundreds of results, add new properties and change rents. Two agents sometimes edit the same property within minutes of each other, and last month one of them overwrote the other's rent change without knowing.

The portal team asked for three things: predictable URLs, errors they can show to users field by field, and protection against silent overwrites.

## How it works

### Resources and methods

The API exposes one **resource**, the property, at `/api/v1/properties`. HTTP methods say what to do with it (@tbl:routes).

::: {#tbl:routes caption="Property API routes"}
| Method and route | Action | Success | Typical failures |
|---|---|---|---|
| `GET /v1/properties?city=&status=&minBedrooms=&pageSize=&continuationToken=` | List, filtered and paged | 200 | 400 bad token |
| `GET /v1/properties/{code}` | Read one | 200 + `ETag` | 404 |
| `POST /v1/properties` | Create | 201 + `Location` | 400, 409 duplicate code |
| `PATCH /v1/properties/{code}` | Change some fields | 200 + new `ETag` | 400, 404, 412 version mismatch |
:::

`PATCH` rather than `PUT`: the portal changes one or two fields at a time, and a partial update can't wipe a field the caller didn't send.

### Problem details

When something fails, the body follows **RFC 9457, Problem Details for HTTP APIs**: a JSON object with `type`, `title`, `status`, `detail` and `instance`, served as `application/problem+json`. Validation failures add an `errors` object keyed by field. Every failure from the API, whatever its cause, has this one shape, so the portal needs one error handler.

### Optimistic concurrency

Dataverse gives every row a **row version** that changes on each update. The API sends it as an **ETag** header. A client that wants to update safely sends it back in `If-Match`. Dataverse then applies the update only if the version still matches; otherwise it refuses with `ConcurrencyVersionMismatch`, which the API returns as **412 Precondition Failed** (@fig:etag). This is **optimistic concurrency**: no locks are held while the agent edits, and conflicts are detected when they happen.

![Optimistic concurrency: the second writer's If-Match no longer matches, so Dataverse refuses the update and the API returns 412.](figures/ch07/etag.svg){#fig:etag}

## Architecture

![Each layer has one job: HTTP details stay in the function, rules in the validator, Dataverse in the service.](figures/ch07/pipeline.svg){#fig:pipeline}

The service never returns HTTP types. It returns an **outcome** (@lst:outcome), and one helper maps outcomes to responses.

```{.csharp #lst:outcome include="src/Harbourline.Connect.Core/Common/Outcome.cs" region="outcome" caption="An Outcome: the result of a service call, independent of HTTP"}
```

## Prerequisites

- Chapter 6: the GET-by-code endpoint works locally and in Azure.
- Properties in your developer environment, ideally more than 50 to see paging.

## Step-by-step implementation

### Step 1 – Define the contract

The contract project holds what callers see (@lst:dto, @lst:requests). Nothing in it refers to Dataverse.

```{.csharp #lst:dto include="src/Harbourline.Connect.Contracts/Properties/PropertyContracts.cs" region="property-dto" caption="The property and page contracts"}
```

```{.csharp #lst:requests include="src/Harbourline.Connect.Contracts/Properties/PropertyContracts.cs" region="property-requests" caption="Create and update requests: every field nullable"}
```

Every request field is nullable on purpose. A missing field and an invalid one must both reach the validator, which reports them; a non-nullable `int` would silently become 0.

### Step 2 – Validate before Dataverse

```{.csharp #lst:validate include="src/Harbourline.Connect.Core/Properties/PropertyValidator.cs" region="property-validate" caption="Validating a create request: all problems, at once"}
```

The validator collects errors rather than stopping at the first. A form with three bad fields then gets three messages in one round trip. Invalid requests never reach Dataverse, so they cost no API calls and count against no limits.

### Step 3 – List with filters and paging

```{.csharp #lst:list include="src/Harbourline.Connect.Core/Properties/PropertyService.cs" region="property-list" caption="Listing properties: filters, a stable order and a continuation token"}
```

1. The continuation token is decoded first; a token that doesn't match this query is rejected (Step 4).
2. Only the columns in the contract are retrieved.
3. The paging cookie from the previous page tells Dataverse where to continue, so rows aren't skipped or repeated when data changes between requests.
4. A unique final sort key, the GUID, makes the order deterministic even when two properties share a name.
5. The next token is issued only when Dataverse says there are more rows.

```{.csharp #lst:filters include="src/Harbourline.Connect.Core/Properties/PropertyService.cs" region="property-filters" caption="Translating query-string filters into conditions"}
```

### Step 4 – Make continuation tokens opaque

The client must not build or edit tokens; it receives a string and sends it back. @lst:token wraps the page number, the paging cookie and a hash of the filters in URL-safe Base64.

```{.csharp #lst:token include="src/Harbourline.Connect.Core/Properties/ContinuationToken.cs" region="continuation-token" caption="Encoding and checking continuation tokens"}
```

1. `Base64Url` (in .NET 9 and later) produces characters that need no escaping in a query string.
2. No token means the first page.
3. A token issued for "Leeds" must not page through "York". The filter hash catches that, and the API answers 400 instead of returning confusing results.

::: note
The token is opaque, not secret: it can be decoded. Never put anything in it that the caller isn't allowed to see. If you need tamper-proof tokens, sign them with HMAC (Chapter 12 shows the technique).
:::

### Step 5 – Map outcomes to HTTP

```{.csharp #lst:results include="src/Harbourline.Connect.Functions/Http/ApiResults.cs" region="api-results" caption="One place that turns outcomes into status codes and problem details"}
```

1. Validation errors become a `ValidationProblemDetails` with an `errors` dictionary, field by field.
2. A version mismatch is 412: the precondition (`If-Match`) failed.
3. `instance` is the request path, so a support engineer can see which call failed.

### Step 6 – The list endpoint

```{.csharp #lst:fnlist include="src/Harbourline.Connect.Functions/Http/PropertyFunctions.cs" region="fn-list" caption="GET /api/v1/properties"}
```

1. Query-string values are parsed defensively; a missing or non-numeric page size uses the default, and the service clamps it to 200.
2. A bad token is the caller's fault: 400 with a problem body, never a 500.

### Step 7 – Partial updates with optimistic concurrency

```{.csharp #lst:patch include="src/Harbourline.Connect.Core/Properties/PropertyMapper.cs" region="property-patch" caption="Building a partial update that carries the row version"}
```

1. The row to update is identified by its alternate key.
2. The caller's version from `If-Match` goes into `RowVersion`.
3. Only fields present in the request are set. Dataverse updates only the columns in the entity, so everything else is untouched.

```{.csharp #lst:update include="src/Harbourline.Connect.Core/Properties/PropertyService.cs" region="property-update" caption="Updating with ConcurrencyBehavior.IfRowVersionMatches"}
```

1. With a version, Dataverse applies the update only if it matches. Without `If-Match`, the update is unconditional, a deliberate choice: Harbourline's batch jobs update without versions, the portal always sends one.
2. Dataverse faults become outcomes: not found and version mismatch are expected business results.
3. Anything else is unexpected and must surface as a failure, so it is rethrown.

```{.csharp #lst:fnpatch include="src/Harbourline.Connect.Functions/Http/PropertyFunctions.cs" region="fn-patch" caption="PATCH /api/v1/properties/{code}"}
```

1. `If-Match` carries the ETag in quotes, as HTTP requires; the quotes are removed before it reaches Dataverse.
2. The response carries the new ETag, so the client can update again without a GET.

### Step 8 – Version the API

The `v1` in every route is the API's major version. A change that would break existing callers, such as renaming a field or changing a status code, goes into a new `v2` route, while `v1` keeps working until its callers move. Adding a field or an optional parameter is not breaking and stays in `v1`. Chapter 14 publishes the versions through API Management, which can also route by header if you prefer URLs without versions.

## Screens you will use

::: {.capture #fig:http-file caption="Exercising the API from an .http file in VS Code or Visual Studio." app="VS Code" path="tests/requests/properties.http" annotations="1 GET list with filters; 2 PATCH with If-Match; 3 412 response with problem details"}
:::

**Expected result:** the second PATCH with the old ETag returns 412 with `"title":"Precondition failed"`.

## Complete working code

- Contracts: `src/Harbourline.Connect.Contracts/Properties/PropertyContracts.cs`
- Core: `Common/Outcome.cs`, `Common/ValidationErrors.cs`, `Properties/*.cs`
- Functions: `Http/ApiResults.cs`, `Http/PropertyFunctions.cs`
- Tests: `tests/.../Properties/*Tests.cs`

## Configuration

No new settings. The maximum page size (200) and the default (50) are constants in `PropertyService` and `PropertyQuery`; change them in code, with a test.

## Sample input and output

```text
PATCH /api/v1/properties/HBR-0042
If-Match: "4826113"
Content-Type: application/json

{"monthlyRent": 1195, "bedrooms": -1}

HTTP/1.1 400 Bad Request
Content-Type: application/problem+json

{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1",
 "title":"The request is not valid.","status":400,
 "instance":"/api/v1/properties/HBR-0042",
 "errors":{"bedrooms":["bedrooms must be 0 to 20."]}}
```

```text
PATCH /api/v1/properties/HBR-0042
If-Match: "4826113"            (someone else has since saved)

HTTP/1.1 412 Precondition Failed
Content-Type: application/problem+json

{"title":"Precondition failed","status":412,
 "detail":"The property was changed by someone else.",
 "instance":"/api/v1/properties/HBR-0042"}
```

(Illustrative responses; exact `type` values depend on the ASP.NET Core version.)

## Testing

The unit tests in `PropertyServiceTests` substitute `IOrganizationServiceAsync2` and check behaviour, not implementation details:

- the list query has the expected page size and conditions, and a token is issued only when there are more rows;
- a version mismatch from Dataverse becomes `PreconditionFailed`, and the update request carried `IfRowVersionMatches`;
- a duplicate key becomes `Conflict`;
- an invalid request never calls Dataverse.

`ContinuationTokenTests` proves tokens round-trip, are URL-safe, and are rejected for a different query. Chapter 19 explains the testing approach in full.

## Common errors and troubleshooting

::: {#tbl:errors caption="Errors you may see in this chapter"}
| Symptom | Likely cause | Fix |
|---|---|---|
| Every PATCH returns 412 | Client sends the ETag without quotes, or an old one | Send the latest `ETag` value exactly |
| 409 on create | Code already exists (`DuplicateRecordEntityKey`) | Expected; show "code already used" |
| Pages repeat or skip rows | Sort order not unique, or cookie not passed | Add the ID as last sort key; pass the cookie |
| 400 "token does not match this query" | Client changed filters but kept the token | Start again without a token |
| 500 with `ConcurrencyVersionNotProvided` | `IfRowVersionMatches` without a version | Only set the behaviour when `If-Match` is present |
:::

## Security considerations

::: security
Validate everything, including values that "can't" be wrong because the portal checks them. The API is reachable by anything that has the key, not only the portal. Never echo Dataverse fault messages to callers: they can contain table names and internal IDs. `ApiResults` returns your own wording.
:::

## Performance and scalability

::: performance
Keep page sizes moderate (50 by default, 200 at most): each page is one Dataverse request, and huge pages make slow responses and large memory spikes. Paging with a cookie keeps deep pages cheaper than page numbers alone, and Chapter 23 goes further with keyset paging.
:::

## Alternatives and design decisions

@tbl:decisions records the choices this chapter made.

::: {#tbl:decisions caption="API design decisions"}
| Decision | Chosen | Alternative | Why |
|---|---|---|---|
| Partial update | `PATCH` with nullable fields | `PUT` of the whole resource | Clients change one field at a time |
| Concurrency | ETag + `If-Match` → row version | Last write wins | Agents overwrote each other |
| Paging | Opaque continuation token | `page=3` | Stable with changing data; clients can't misuse it |
| Errors | RFC 9457 problem details | Custom error JSON | A standard every HTTP client library understands |
| Versioning | `v1` in the route | Header or media type | Visible in logs and APIM, easy for portal developers |
:::

## Best practices

- Keep contracts free of Dataverse types; map at the edge.
- Validate everything and report all errors at once.
- Sort by a unique key last, and always page with the cookie.
- Return 412 for version conflicts and include the new ETag on success.
- Use one error shape, RFC 9457, for every failure.
- Put a major version in the route from day one.

## Hands-on exercise

::: exercise
**Goal:** add sorting to the list endpoint.

**Steps:** accept `sort=name` or `sort=rent` (ascending only). Include the sort in the filter hash, so tokens can't be reused across sort orders. Keep the ID as the final sort key.

**Done when:** a unit test proves the query has the right order expressions, and a token issued for `sort=name` is rejected for `sort=rent`.

**Stretch:** add `DELETE /v1/properties/{code}` that sets status to *Maintenance* instead of deleting, and argue why a soft delete fits Harbourline better.
:::

## Knowledge check

::: knowledge-check
1. Why does the API use `PATCH` with nullable fields?
2. What must a client send to update safely, and what happens if someone else saved first?
3. Why does the list order end with the property's GUID?
4. What is in a continuation token, and why does it include a hash of the filters?
5. Which kinds of change require a new API version?
:::

Answers are in Appendix G.

::: summary
- One resource, standard methods and clear status codes make the API predictable.
- Validation runs before Dataverse and returns every problem in one RFC 9457 response.
- Server-side paging uses Dataverse's paging cookie, wrapped in an opaque, filter-bound token.
- ETags carry row versions; `If-Match` and `IfRowVersionMatches` turn silent overwrites into 412s.
- Services return outcomes; one helper turns them into HTTP.
:::

## References

- [RFC 9457: Problem Details for HTTP APIs](https://www.rfc-editor.org/rfc/rfc9457) – IETF, checked 2026-10-27.
- [Optimistic concurrency](https://learn.microsoft.com/power-apps/developer/data-platform/optimistic-concurrency) – Microsoft Learn, checked 2026-10-27.
- [Page large result sets with QueryExpression](https://learn.microsoft.com/power-apps/developer/data-platform/org-service/queryexpression/page-results) – Microsoft Learn, checked 2026-10-27.
- [Azure Functions HTTP trigger](https://learn.microsoft.com/azure/azure-functions/functions-bindings-http-webhook-trigger) – Microsoft Learn, checked 2026-10-27.
- [Web service error codes](https://learn.microsoft.com/power-apps/developer/data-platform/reference/web-service-error-codes) – Microsoft Learn, checked 2026-10-27.

<!-- Glossary terms introduced in this chapter: resource, problem details, row version, ETag, optimistic concurrency, continuation token, API versioning -->
