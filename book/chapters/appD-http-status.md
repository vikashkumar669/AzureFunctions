---
chapter: "D"
appendix: true
title: "HTTP Status Code Guide for Integration APIs"
---

How Harbourline's APIs use status codes, and how its clients react to them.

::: {#tbl:statuses caption="Status codes returned by Harbourline Connect"}
| Status | Meaning in Harbourline APIs | Body | Client should |
|---|---|---|---|
| 200 OK | Success with content; also imports with row errors | Resource or result | Use it |
| 201 Created | Resource created | Resource; `Location`, `ETag` | Store the ETag |
| 202 Accepted | Queued for processing | Status URL | Poll the status URL |
| 400 Bad Request | Invalid JSON, validation errors, bad token or cursor | Problem details (+ `errors`) | Fix the request; don't retry unchanged |
| 401 Unauthorized | Missing or invalid credentials | Problem details (gateway) or empty | Get a token; check audience and roles |
| 403 Forbidden | Authenticated but not allowed (e.g., wrong organization on a webhook) | Empty or problem details | Don't retry |
| 404 Not Found | Resource doesn't exist *or* caller may not see it | Problem details | Don't retry |
| 409 Conflict | Duplicate key on create | Problem details | Use the existing resource |
| 412 Precondition Failed | `If-Match` doesn't match the current version | Problem details | Re-read, merge, retry with the new ETag |
| 413 Payload Too Large | Upload over the limit | Problem details | Split or shrink |
| 429 Too Many Requests | Gateway rate limit | `Retry-After` | Wait, then retry |
| 500 Internal Server Error | Unexpected failure | Problem details with trace ID | Retry later if idempotent; report the trace ID |
| 503 Service Unavailable | Dependency down (rare at our edge) | Problem details | Retry with backoff |
:::

## Status codes Harbourline receives, and what it does

::: {#tbl:received caption="Handling statuses from partners and Dataverse"}
| From | Status | Treated as | Action |
|---|---|---|---|
| Partner | 408, 429, 5xx, no response | Transient | Retry with backoff (idempotent calls only); circuit breaker |
| Partner | 400, 401, 403, 404, 409, 422 | Permanent | Dead-letter or report; no retry |
| Dataverse Web API | 429 + `Retry-After` | Throttling | Wait, then retry (`ServiceClient` does this) |
| Dataverse | 412 | Version mismatch | Map to 412 for our caller |
| Webhook caller (Dataverse) | We return 2xx | Success | Dataverse ignores the body |
| Webhook caller (Dataverse) | We return 502/503/504 | Retry once | Use for genuine temporary failures only |
:::
