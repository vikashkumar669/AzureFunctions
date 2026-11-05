---
chapter: 14
part: "Part IV — Advanced Enterprise Implementations"
title: "Building a Secure Integration API"
build-summary: "Maintenance partners reach Harbourline's Partner API only through API Management, with a Microsoft Entra ID token and a subscription key; the Function App accepts calls from API Management alone, and nothing leaks when something fails."
builds:
  - "The Partner API in API Management, defined contract-first with OpenAPI"
  - "A policy that validates Entra ID tokens, rate-limits per partner and calls the backend with a managed identity"
  - "App Service authentication that locks the Function App to API Management"
versions: "Azure API Management (Consumption tier) · App Service authentication · Microsoft Entra ID · OpenAPI 3.0"
---

::: objectives
- Explain what API Management adds in front of Azure Functions.
- Register an API and its client apps in Microsoft Entra ID with app roles.
- Publish an API contract-first from OpenAPI, and apply inbound policies for tokens, rate limits and payload size.
- Call the Function App from API Management with a managed identity, and lock the app with App Service authentication.
- Return safe errors that help callers without exposing internals.
- Choose how Power Pages and other first-party consumers should call Harbourline.
:::

## The business problem

Harbourline is opening its order intake (Chapter 24) and property search (Chapter 23) to maintenance partners. Partners are other companies: Harbourline can't trust their networks, can't see their code, and must be able to cut one of them off without affecting the others.

The function keys used so far are fine inside Harbourline. For partners they fall short: a key is shared by everyone who has it, can't express "this partner may submit orders but not read properties", and can't throttle one noisy caller.

## How it works

### API Management as the front door

**Azure API Management (APIM)** is a gateway in front of your APIs. Callers see APIM's URL; APIM applies **policies** to each request (XML rules for authentication, throttling, transformation) and forwards it to the **backend**, here the Function App. Its developer portal publishes documentation, and **products** and **subscriptions** give each partner its own key and limits.

### OAuth 2.0 with Microsoft Entra ID

Each partner's system is an app registration in Entra ID (in Harbourline's tenant, or theirs if they are multi-tenant). It gets an access token with the **client credentials** flow and sends it as `Authorization: Bearer …`. The Partner API's own app registration defines **app roles**, such as `Orders.Submit` and `Properties.Read`; an administrator grants each partner only the roles it needs, and the roles appear in the token's `roles` claim.

APIM's `validate-azure-ad-token` policy checks the token's signature, issuer, audience and expiry, and the required roles, before anything reaches your code.

### Locking the backend

If the Function App stayed reachable directly, the gateway would be optional. Two measures make it mandatory. APIM calls the backend with a token for *its own* managed identity (`authentication-managed-identity`). And **App Service authentication** (often called Easy Auth), configured on the Function App, rejects any request without a valid token for the app, and accepts only API Management's identity as a client.

![Partners never reach the Function App directly: API Management checks their token, key and rate, then calls the app with its own identity.](figures/ch14/gateway.svg){#fig:gateway}

## Architecture

::: {#tbl:components caption="Components in @fig:gateway"}
| Component | Role | Authenticates with | Failure path |
|---|---|---|---|
| Partner app | Calls the Partner API | Entra ID token (client credentials) + subscription key | 401 / 403 / 429 from APIM |
| Partner API app registration | Defines audience and app roles | — | Token rejected if role missing |
| API Management | Validates, throttles, forwards | Managed identity to the backend | Problem response from `on-error` |
| Function App with Easy Auth | Runs the API | Accepts only APIM's identity | 401 for direct calls |
:::

## Prerequisites

- Chapters 7 and 24's endpoints deployed (orders, search, properties).
- Permission to create app registrations in Microsoft Entra ID.
- An API Management instance; the Bicep template creates one on the Consumption tier.

## Step-by-step implementation

### Step 1 – Register the API and its roles

In the Microsoft Entra admin center, register **Harbourline Partner API**. Under **Expose an API**, set the Application ID URI, for example `api://harbourline-partner-api`. Under **App roles**, add `Orders.Submit` and `Properties.Read`, allowed member type *Applications*.

For each partner, register a client app (or accept theirs), then under the Partner API's **Enterprise application › Permissions**, or the client's **API permissions**, grant the application permissions it needs and grant admin consent.

### Step 2 – Write the contract first

The OpenAPI file `infra/apim/partner-api.openapi.yaml` is the contract: paths, request and response schemas, and status codes, including problem details for errors. It is written by hand and reviewed like code, *before* the functions change. Microsoft's OpenAPI extension for Azure Functions, which generated definitions from attributes, is in maintenance mode, and a hand-written contract is a better review artifact anyway: partners read it, and a change to it is visibly a change to the deal.

### Step 3 – Publish the API from Bicep

```{.bicep #lst:apim include="infra/apim.bicep" region="bicep-apim-api" caption="Importing the OpenAPI contract and attaching the policy"}
```

1. `loadTextContent` reads the OpenAPI file at deployment time, so the published API always matches the reviewed contract.
2. The policy is a separate XML file, also reviewed as code.

Each partner gets a **subscription** to a product containing the API. The subscription key identifies the partner for rate limiting and lets you suspend one partner without touching the others.

### Step 4 – Write the inbound policy

```{.xml #lst:policy include="infra/apim/partner-api-policy.xml" caption="The Partner API policy"}
```

1. `validate-azure-ad-token` accepts only tokens from Harbourline's tenant, issued for the Partner API's audience, with one of the API's roles. Everything else gets 401 here, at the gateway.
2. `rate-limit` allows 60 calls a minute per subscription. Callers over the limit get 429 with a `Retry-After` header. Note that `rate-limit-by-key`, which can throttle by any value such as a token claim, is not available on the Consumption tier; with a subscription per partner, `rate-limit` gives the same per-partner effect.
3. Oversized bodies are refused before they reach the backend.
4. APIM gets a token for the Function App's audience with its own managed identity and forwards the call.
5. The `on-error` section replaces any error body with a minimal problem response and a trace ID that support can look up.

### Step 5 – Lock the Function App

On the Function App, open **Settings › Authentication › Add identity provider**, choose **Microsoft**, and create a new app registration for the Function App (its Application ID URI is the `function-app-audience` named value in the policy). Set:

- **Restrict access:** Require authentication.
- **Unauthenticated requests:** HTTP 401.
- **Client application requirement:** allow requests only from specific client applications, and add API Management's managed identity's application ID.

Direct calls to the Function App now fail with 401, even with a valid function key. Leave the health endpoint excluded if your monitoring needs it, using the authentication settings' excluded paths.

### Step 6 – Validate input again in the function

APIM validates who calls; the function still validates *what* they send. The order and search functions use the same validators as everywhere else (Chapters 7 and 24). Defence in depth means a gateway misconfiguration doesn't open the data.

### Step 7 – Call it as a partner

```bash
# 1. Get a token (client credentials)
curl -s -X POST \
  https://login.microsoftonline.com/<tenant-id>/oauth2/v2.0/token \
  -d client_id=<partner-app-id> -d client_secret=<partner-secret> \
  -d scope=api://harbourline-partner-api/.default \
  -d grant_type=client_credentials
# 2. Call the API
curl -i https://<apim>.azure-api.net/partner/v1/search/properties?city=Leeds \
  -H "Authorization: Bearer <token>" \
  -H "Ocp-Apim-Subscription-Key: <partner-key>"
```

The partner's secret lives in the partner's system, not Harbourline's. Encourage partners to use certificates or, if they run on Azure, their own managed identity with workload identity federation.

## Deep dive: defence in depth for a public API

No single control protects a public API. Harbourline's partner API has five layers, and each one assumes the layer before it might fail. Reading the layers from the outside in is a useful review technique for any API you're asked to approve.

::: {#tbl:layers caption="The layers protecting the partner API"}
| Layer | Control | Stops | Configured in |
|---|---|---|---|
| 1. Gateway identity | Subscription key per partner | Unknown callers; lets you revoke one partner | API Management product and subscription |
| 2. Caller identity | Entra ID token with audience and role | Stolen or shared keys used without a token; callers without permission | `validate-azure-ad-token` policy |
| 3. Traffic shaping | Rate limit and body-size check | Floods, accidental loops, oversized payloads | `rate-limit`, `choose` in the policy |
| 4. Backend lock | Easy Auth on the Function App, accepting only API Management's identity | Calls that bypass the gateway | Function App authentication settings |
| 5. Application | Validation and authorization in the function | Bad data, and roles checked again in code | `ClientPrincipal`, validators |
:::

### Why both a key and a token?

They answer different questions. The **subscription key** says *which partner contract* the call belongs to: it drives rate limits, analytics and revocation per partner, and it's easy for partners to handle. The **token** proves *who is calling*: it's short-lived, issued by your tenant, and can't be reused for another API, because its audience is fixed. A leaked key alone is useless without a valid token; a token alone can't get past the gateway without a subscription.

### What the token checks actually check

`validate-azure-ad-token` verifies the token's **signature** against your tenant's published signing keys, its **issuer** (your tenant), its **lifetime** (`exp`, `nbf`), its **audience** (the partner API's application ID URI), and the **required claims** you list. Harbourline requires a `roles` claim containing one of the API's app roles. App roles are assigned to each partner's client application by an administrator, so granting or withdrawing a partner's access is an Entra ID change, not a code change.

### Rate limits on the Consumption tier

The Consumption tier of API Management supports the `rate-limit` policy, which counts calls **per subscription**, but not `rate-limit-by-key`, which counts by an expression such as the caller's IP address or client ID. Because every partner has its own subscription, per-subscription limits are what Harbourline needs. When a partner exceeds the limit, the gateway answers **429 Too Many Requests** with a `Retry-After` header, and the call never reaches the Function App. Rate limits in API Management are approximate across gateway instances; treat them as protection against abuse, not as billing-grade quotas.

### Locking the backend

Without layer 4, anyone who discovers the Function App's host name could call it directly and skip layers 1–3. Easy Auth (App Service authentication) on the Function App requires a valid Entra ID token for every request and lets you restrict the allowed client applications to the API Management instance's managed identity. API Management gets that token with `authentication-managed-identity`. For stricter isolation, add network restrictions (private endpoints or access restrictions) so the Function App isn't reachable from the internet at all; that needs a tier of API Management that supports virtual networks.

### Errors that don't leak

The `on-error` section returns a small problem-details body with a **trace ID** and no exception text. Partners can quote the trace ID to Harbourline support, who find the full story in Application Insights (Chapter 18). Never return stack traces, Dataverse error messages or internal host names to external callers; they help attackers far more than they help partners.

::: security
Review the layers whenever something changes: a new operation, a new partner type, a tier change. The commonest real-world failure isn't a broken layer; it's a new endpoint added to the Function App with `AuthorizationLevel.Anonymous` and no gateway in front of it.
:::

## Screens you will use

::: {.capture #fig:app-roles caption="App roles on the Partner API registration." app="Microsoft Entra admin center" path="App registrations › Harbourline Partner API › App roles" annotations="1 Orders.Submit; 2 Properties.Read; 3 Allowed member types: Applications"}
:::

::: {.capture #fig:easyauth caption="App Service authentication accepts only API Management's identity." app="Azure portal" path="Function App › Settings › Authentication › Microsoft provider › Edit" annotations="1 Require authentication; 2 Return HTTP 401; 3 Allowed client applications"}
:::

::: {.capture #fig:apim-test caption="Testing an operation in API Management with a token and subscription key." app="Azure portal" path="API Management › APIs › Harbourline Partner API › SearchProperties › Test" annotations="1 Authorization header; 2 Subscription key; 3 Response 200; 4 Trace"}
:::

## Complete working code

- `infra/apim.bicep`, `infra/apim/partner-api.openapi.yaml`, `infra/apim/partner-api-policy.xml`
- `src/Harbourline.Connect.Functions/Http/ClientPrincipal.cs`: reading the caller from Easy Auth (Chapter 22).

## Configuration

::: {#tbl:settings caption="Settings for the secure API"}
| Setting | Where | Value | Secret? |
|---|---|---|---|
| `tenant-id` | APIM named value | Harbourline's tenant ID | No |
| `partner-api-audience` | APIM named value | `api://harbourline-partner-api` | No |
| `function-app-audience` | APIM named value | Function App registration's ID URI | No |
| Partner subscription keys | APIM subscriptions | One per partner | Yes, held by partners |
:::

## Sample input and output

```text
GET /partner/v1/search/properties?city=Leeds    (no token)

HTTP/1.1 401 Unauthorized
Content-Type: application/problem+json

{"title":"The request could not be completed.","status":401,
 "traceId":"6f1e2c4a-…"}
```

## Testing

Test the gateway as a partner would: no token (401), a token without the needed role (401), a valid token without a subscription key (401), 61 calls in a minute (429 on the last), and a direct call to the Function App (401 from Easy Auth). Keep these as a script in the repository and run it after every policy change.

## Common errors and troubleshooting

::: {#tbl:errors caption="Errors you may see with the secure API"}
| Symptom | Likely cause | Fix |
|---|---|---|
| 401 with a fresh token | Wrong audience (`aud`) or missing role | Request the API's `/.default` scope; grant the role and consent |
| 401 from the Function App behind APIM | APIM's identity not in allowed clients, or wrong resource in `authentication-managed-identity` | Match the app's ID URI; allow APIM's app ID |
| 429 for a partner | Over 60 calls a minute | Expected; partner should honour `Retry-After` |
| Policy rejected at deployment | Unsupported policy for the tier | Check the policy's tier support; e.g. `rate-limit-by-key` isn't on Consumption |
| Partners see backend stack traces | No `on-error` handling | Return problem details from `on-error` |
:::

## Security considerations

::: security
Make the gateway the only way in. Easy Auth restricted to APIM's identity removes the bypass; on Premium or Dedicated plans, private endpoints and network restrictions can do the same at network level. Review partner role assignments and subscriptions on a schedule, and remove them when a contract ends.
:::

## Performance and scalability

::: performance
The Consumption tier of APIM scales automatically and bills per call, which suits a partner API with modest traffic. Policies run on every request: keep expressions simple. Response caching in APIM (Chapter 23) can remove load from Dataverse entirely for popular read operations.
:::

## Alternatives and design decisions

Without APIM, Easy Auth alone could validate partner tokens on the Function App, and roles could be checked in code. That works for one or two internal callers; it gives no per-partner keys, quotas, developer portal or central policy. Harbourline expects more partners, so the gateway pays for itself.

For **Power Pages**, Harbourline's tenant portal, there is a better first-party route for Dataverse data: the Power Pages Web API and table permissions read Dataverse directly with the signed-in contact's permissions. Use Harbourline Connect only for what Dataverse alone can't do, such as calling LedgerOne, and call it through APIM from a server-side component, never from browser code with a stored key.

## Best practices

- Put a gateway in front of APIs for external callers; make it the only way in.
- Define APIs contract-first in OpenAPI and review the contract.
- Use Entra ID tokens with app roles; give each partner its own subscription.
- Validate tokens, sizes and rates at the gateway, and inputs again in code.
- Return problem details with a trace ID from `on-error`; never internal messages.
- Check that every policy you use is supported on your APIM tier.

## Guided lab: call the partner API as a partner

**Requires:** the API Management instance and API from Step 3, the Function App locked as in Step 5, and permission in Entra ID to create an app registration in your development tenant. The lab creates a test client called `northwind-lettings-test`.

### Lab 14.1 – Create a test partner

```bash
CLIENT_ID=$(az ad app create --display-name northwind-lettings-test --query appId -o tsv)
az ad sp create --id $CLIENT_ID
SECRET=$(az ad app credential reset --id $CLIENT_ID --display-name lab --years 1 \
  --query password -o tsv)
```

Then assign the app role **Properties.Read** of *Harbourline Partner API* to the new service principal: **Enterprise applications › Harbourline Partner API › Users and groups**, or **App registrations › northwind-lettings-test › API permissions › Add a permission › My APIs**, then *Grant admin consent*.

::: security
A client secret is acceptable for a short-lived lab client in a development tenant. Real partners use certificates or federated credentials, and you delete this client at the end of the lab.
:::

### Lab 14.2 – Get a token as the partner

```bash
TENANT=$(az account show --query tenantId -o tsv)
TOKEN=$(curl -s -X POST "https://login.microsoftonline.com/$TENANT/oauth2/v2.0/token" \
  -d "client_id=$CLIENT_ID" -d "client_secret=$SECRET" \
  -d "grant_type=client_credentials" -d "scope=api://harbourline-partner-api/.default" \
  | jq -r .access_token)
```

Decode it as in Lab 5.1. **Checkpoint:** `aud` is the partner API, `roles` contains `Properties.Read`, and there is no `scp` claim: this is an app-only token.

### Lab 14.3 – Create a subscription and call the API

In API Management, create a subscription for the partner product (or for the API) named `northwind-lettings-test` and copy its primary key into `KEY`. Then call the search operation:

```bash
GW=https://<apim-name>.azure-api.net/partner
curl -i "$GW/v1/search/properties?city=Leeds&size=5" \
  -H "Authorization: Bearer $TOKEN" -H "Ocp-Apim-Subscription-Key: $KEY"
```

**Checkpoint:** `200 OK` with up to five properties. The response has no `X-Powered-By` header; the outbound policy removed it.

### Lab 14.4 – Remove one layer at a time

Repeat the call four times, each time breaking one thing:

| Change | Expected status | Stopped by |
|---|---|---|
| Omit `Ocp-Apim-Subscription-Key` | 401 | Gateway subscription check |
| Omit `Authorization` | 401 | Token validation |
| Use a token for another audience (`az account get-access-token --query accessToken -o tsv`) | 401 | Token validation (audience) |
| Call the Function App host name directly with the partner token | 401 or 403 | Easy Auth on the backend |

**Checkpoint:** every variant fails, and none of them appears as a function invocation in Application Insights, except possibly as a rejected request at the Function App's front end.

### Lab 14.5 – Hit the rate limit

```bash
for i in $(seq 70); do
  curl -s -o /dev/null -w "%{http_code} " "$GW/v1/search/properties?city=Leeds&size=1" \
    -H "Authorization: Bearer $TOKEN" -H "Ocp-Apim-Subscription-Key: $KEY"
done; echo
```

**Checkpoint:** the first calls return 200; at around the 60th call within the minute, responses change to **429**. Wait a minute and call once more: 200 again. (The exact switching point can vary slightly, because the limit is approximate.)

### Lab 14.6 – Withdraw access

Remove the **Properties.Read** role assignment from the test client, get a new token (Lab 14.2) and call again. **Checkpoint:** `401`, because the token no longer carries a required role. No deployment was needed to revoke a partner.

### Lab 14.7 – Clean up

```bash
az ad app delete --id $CLIENT_ID
```

Delete the `northwind-lettings-test` subscription in API Management too.

## Hands-on exercise

::: exercise
**Goal:** add a read-only partner.

**Steps:** create a client app that has only `Properties.Read`, give it its own subscription, and run the gateway test script with it.

**Done when:** it can search properties, and submitting an order returns 401 at the gateway (add a `required-claims` check per operation, or split the policy by operation).

**Stretch:** add a `quota` for the read-only partner of 10,000 calls a week, and decide what status code and message it should get when it runs out.
:::

## Knowledge check

::: knowledge-check
1. What does API Management add that function keys can't?
2. How does a partner's token prove it may submit orders?
3. Why is `rate-limit` used instead of `rate-limit-by-key` in this design?
4. What stops a partner from calling the Function App directly?
5. Why does the function validate requests that APIM already let through?
:::

Answers are in Appendix G.

::: summary
- API Management is the front door: token checks, keys, rate limits and safe errors in policy.
- Partners authenticate with Entra ID tokens carrying app roles; each has its own subscription.
- The OpenAPI contract is written first, reviewed, and deployed with Bicep.
- APIM calls the Function App with its managed identity; Easy Auth accepts no one else.
- Validation still happens in code; the gateway is one layer, not the only one.
:::

## References

- [About API Management](https://learn.microsoft.com/azure/api-management/api-management-key-concepts) – Microsoft Learn, checked 2026-10-27.
- [API Management policy reference](https://learn.microsoft.com/azure/api-management/api-management-policies) – Microsoft Learn, checked 2026-10-27.
- [Validate Microsoft Entra token policy](https://learn.microsoft.com/azure/api-management/validate-azure-ad-token-policy) – Microsoft Learn, checked 2026-10-27.
- [Limit call rate by subscription](https://learn.microsoft.com/azure/api-management/rate-limit-policy) – Microsoft Learn, checked 2026-10-27.
- [Authentication and authorization in Azure App Service and Azure Functions](https://learn.microsoft.com/azure/app-service/overview-authentication-authorization) – Microsoft Learn, checked 2026-10-27.
- [Add app roles to your application and receive them in the token](https://learn.microsoft.com/entra/identity-platform/howto-add-app-roles-in-apps) – Microsoft Learn, checked 2026-10-27.
- [Power Pages Web API overview](https://learn.microsoft.com/power-pages/configure/web-api-overview) – Microsoft Learn, checked 2026-10-27.

<!-- Glossary terms introduced in this chapter: API Management, policy, backend, product, subscription key, client credentials flow, app role, Easy Auth, contract-first -->

<!-- expanded: deep dive + guided lab -->
