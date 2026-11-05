---
chapter: 22
part: "Part V — Capstone Projects"
title: "Project 2 — Secure Excel Export API"
build-summary: "Lettings agents download a lease pack (lease summary and invoice history in one workbook) and get exactly the data Dynamics 365 would show them, because the function reads Dataverse as them."
builds:
  - "User authentication with App Service authentication and the caller's object ID"
  - "Dataverse impersonation through a cloned ServiceClient"
  - "Direct download for small files, user delegation SAS links for large ones"
versions: ".NET 10 · Dataverse.Client 1.2.27 · ClosedXML 0.105.1 · Azure.Storage.Blobs (via the Blobs extension)"
---

::: objectives
- Identify the signed-in user in a function behind App Service authentication.
- Apply Dataverse security to an export by impersonating the caller.
- Decide between returning a file and returning a short-lived link.
- Issue user delegation SAS tokens without storage account keys.
- Recognise the privileges and risks impersonation brings.
:::

## The business problem

Agents prepare a "lease pack" before every tenant meeting: the lease and every invoice, in Excel. Today an administrator builds it from an export, and because administrators see everything, agents have twice received packs containing another branch's leases.

The rule from security is short: an agent's pack must contain only what that agent can open in Dynamics 365. Copying Dataverse's security rules into the function would drift; the function must *use* them.

## How it works

### Who is calling

The agents' browser app signs them in with Microsoft Entra ID and calls the function with the user's token. **App Service authentication** on the Function App validates the token and passes the user's identity to the code in the `X-MS-CLIENT-PRINCIPAL` header: Base64-encoded JSON with the token's claims. The platform sets this header itself and removes any copy a caller sends, so code can trust it when authentication is enabled.

### Acting as the user

Dataverse supports **impersonation**: a caller with the *Act on Behalf of Another User* privilege (`prvActOnBehalfOfAnotherUser`) can run requests as another user, identified by their Entra object ID. The effective privileges are the intersection of both users' privileges. Microsoft requires the privilege to be assigned directly to the user, not through a team.

`ServiceClient.CallerAADObjectId` sets the impersonated user. Because the shared client is used by every invocation, the function **clones** it, sets the caller on the clone, and disposes it after the request.

### Delivering the file

Small workbooks go back in the HTTP response. Big ones would hold the response open, and the HTTP limit is 230 seconds. Those are uploaded to Blob Storage, and the user gets a link carrying a **user delegation SAS**: a signed, time-limited, read-only token issued with an Entra ID-backed key instead of the storage account key, which is disabled anyway (Chapter 20).

![The function reads Dataverse as the signed-in agent, so Dynamics 365 security decides what goes in the workbook; big files leave through a 15-minute read-only link.](figures/ch22/pack.svg){#fig:pack}

## Architecture

::: {#tbl:components caption="Lease pack components"}
| Component | Role | Authenticates with | Failure path |
|---|---|---|---|
| App Service authentication | Validates the agent's token | Entra ID | 401 before the function runs |
| `GetLeasePack` | Orchestrates | Reads caller from header | 401 without principal |
| `ServiceClientUserScope` | Clones the client as the caller | App's identity + impersonation privilege | 404 if the agent can't see the lease |
| `lease-packs` container | Large packs | App identity (Blob Data Owner, which includes delegation) | — |
:::

## Prerequisites

Chapter 14 (App Service authentication), Chapter 17 (ClosedXML), Chapter 5 (roles). Add *Act on Behalf of Another User* to a separate role assigned directly to the application user, so it is visible and reviewable on its own.

## Step-by-step implementation

### Step 1 – Read the caller

```{.csharp #lst:principal include="src/Harbourline.Connect.Functions/Http/ClientPrincipal.cs" region="client-principal" caption="The caller's Entra object ID from App Service authentication"}
```

1. No header means authentication isn't configured or the call bypassed it: the function returns 401.
2. The header is Base64-encoded JSON.
3. The object ID claim may appear under its long URI or as `oid`.

### Step 2 – Run Dataverse calls as the caller

```{.csharp #lst:scope include="src/Harbourline.Connect.Core/Dataverse/UserScope.cs" region="user-scope" caption="A cloned client that impersonates the caller"}
```

1. `Clone` creates a client sharing the authenticated connection; it is disposed after the work.
2. Every request from the clone runs as the caller, with their roles and row access.

### Step 3 – Build the pack

```{.csharp #lst:pack include="src/Harbourline.Connect.Core/Exports/LeasePackService.cs" region="lease-pack" caption="The lease pack, built entirely with the caller's access"}
```

1. A lease the agent can't see returns "not found", the same as one that doesn't exist. Answering "forbidden" would confirm that the lease exists.
2. The invoices are queried with the same impersonated client, so row-level security applies to them too.

### Step 4 – Return the file or a link

```{.csharp #lst:packfn include="src/Harbourline.Connect.Functions/Exports/LeasePackFunction.cs" region="pack-function" caption="GET /api/v1/leases/{number}/pack"}
```

1. The host-level authorization is anonymous because Easy Auth already rejected unauthenticated callers; the function still checks the principal is present.
2. All data access happens as the caller.
3. Up to 2 MB, the workbook goes back directly with a download file name.
4. Above that, a link.

```{.csharp #lst:sas include="src/Harbourline.Connect.Functions/Exports/LeasePackFunction.cs" region="user-delegation-sas" caption="Uploading and issuing a 15-minute, read-only user delegation SAS"}
```

1. The **user delegation key** is requested with the app's identity; no account key exists.
2. The SAS covers one blob (`b`), not the container.
3. Read only, 15 minutes.

Add a lifecycle management rule on `lease-packs` that deletes blobs after one day.

## Deep dive: two ways to act as the user

The lease pack must contain only what the requesting agent may see. There are two established ways for a back-end service to call Dataverse "as" a person, and they suit different situations.

::: {#tbl:actas caption="Impersonation compared with the on-behalf-of flow"}
| | Impersonation (this chapter) | On-behalf-of (OBO) flow |
|---|---|---|
| How | The app signs in as itself and sets `CallerAADObjectId` | The app exchanges the user's token for a Dataverse token for that user |
| Who Dataverse sees | The user, with the app recorded as the delegate | The user only |
| Privilege needed | *Act on Behalf of Another User* for the application user | None beyond the user's own |
| Needs the user's token | No, only their object ID from a trusted source | Yes, and an app registration with a secret or certificate |
| Works for background jobs | Yes | Only while a user token is available |
| Main risk | The app can act as **any** user; the object ID must come from a trusted place | Token handling and consent configuration |
:::

Harbourline chose impersonation because the Function App already has a managed identity and no secrets, and because Easy Auth provides a trustworthy object ID. OBO is a good choice when you can't grant the impersonation privilege, or when a policy requires that the user's own token be used end to end.

### Trusting the object ID

Impersonation is only as safe as the object ID you pass to it. `ClientPrincipal` reads `X-MS-CLIENT-PRINCIPAL`, a header that App Service authentication sets **after validating the user's token**, and that it strips from incoming requests. With Easy Auth enabled, the header is trustworthy. **Without** Easy Auth (on your laptop, or if someone turns it off), anyone can send the header and choose whose identity the app assumes. Treat "Easy Auth is on and requires authentication" as part of this function's security design, check it in your readiness review (Chapter 25), and consider failing closed when the app runs in Azure without it.

### What Dataverse records

When the app impersonates a user, Dataverse applies **that user's** security roles to every request, so the lease query returns nothing the agent couldn't open in Dynamics 365. Writes made while impersonating record the user as *Created By* or *Modified By*, and the application user as the delegate (*Created By (Delegate)*, the `createdonbehalfby` column). Auditing therefore shows both who asked and which system acted. The lease pack only reads, but the same pattern is used for writes elsewhere.

### The privilege is powerful

*Act on Behalf of Another User* (`prvActOnBehalfOfAnotherUser`) lets the holder act as **any** user, including administrators. Keep it in its own security role (*Harbourline Impersonation*), assign that role directly to the one application user that needs it, never through a team, and review the assignment regularly. Code that impersonates should do it in one place (`UserScope` in `src/Harbourline.Connect.Core/Dataverse/UserScope.cs`) so a review can find every use.

### Delivering files safely

Small packs go back in the response; larger ones are uploaded and returned as a link with a **user delegation SAS**: signed with a key obtained by the Function App's identity, read-only, for one blob, valid for 15 minutes. No storage account key is involved, and the link stops working on its own. Combine it with a lifecycle rule that deletes packs after a day, so links and files don't accumulate.

## Screens you will use

::: {.capture #fig:pack-xlsx caption="A lease pack opened in Excel." app="Microsoft Excel" path="lease-pack-LSE-0042.xlsx" annotations="1 Lease summary sheet; 2 Invoices sheet; 3 Formatted amounts"}
:::

::: {.capture #fig:impersonation-role caption="The impersonation privilege in its own role, assigned directly to the application user." app="Power Platform admin center" path="Security roles › Harbourline Impersonation › Business Management" annotations="1 Act on Behalf of Another User: Organization; 2 Assigned directly, not via a team"}
:::

## Complete working code

`src/Harbourline.Connect.Core/Dataverse/UserScope.cs`, `Exports/LeasePackService.cs`, `src/Harbourline.Connect.Functions/Http/ClientPrincipal.cs`, `Exports/LeasePackFunction.cs`.

## Configuration

`Exports__BlobServiceUri` (the account's blob endpoint), set by Bicep. App Service authentication with the agents' app registration, *Require authentication*, HTTP 401 for unauthenticated requests.

## Sample input and output

```text
GET /api/v1/leases/LSE-0042/pack          (as agent in the Leeds branch)
HTTP/1.1 200 OK
Content-Disposition: attachment; filename=lease-pack-LSE-0042.xlsx

GET /api/v1/leases/LSE-0917/pack          (a York lease)
HTTP/1.1 404 Not Found
{"title":"Not found","status":404,"detail":"Lease LSE-0917 was not found."}
```

## Testing

Unit-test `LeasePackService` with a substitute `IDataverseUserScope` that runs the work against a substitute `IOrganizationServiceAsync2`: no lease gives `NotFound`, a lease gives a workbook with two sheets. The security behaviour can only be proved in an environment: sign in as two agents from different business units and check that each sees only their own leases.

## Common errors and troubleshooting

::: {#tbl:errors caption="Errors you may see with the export API"}
| Symptom | Likely cause | Fix |
|---|---|---|
| 401 for signed-in users | Authentication not configured or token for another audience | Configure Easy Auth with the right app registration |
| `PrivilegeDenied` on every call | App user lacks *Act on Behalf of Another User*, or it comes via a team | Assign the privilege directly |
| Agent sees nothing | Agent has no Dynamics 365 user or role in this environment | Add the user and role |
| SAS link returns 403 | Key not delegated, or link expired | Grant Blob Data roles; check clock and expiry |
:::

## Security considerations

::: security
Impersonation is powerful: with it, the application user can act as anyone it can name. Grant it in a dedicated role, document why, and keep the code that sets `CallerAADObjectId` in one place, fed only by the validated principal header, never by a request parameter.
:::

## Performance and scalability

::: performance
Cloning is cheaper than creating a client, but not free; it happens once per request here. Packs are small; if one grows, generate it in a queued function and notify the agent with the link.
:::

## Alternatives and design decisions

The function could query as itself and filter by business unit in code, but that duplicates Dataverse's security model, which changes. **On-behalf-of** token exchange, where the function exchanges the user's token for a Dataverse token as the user, is another route that needs no impersonation privilege; it requires the app to be configured for delegated Dataverse access and token handling in code. Harbourline chose impersonation because the app already holds a working Dataverse connection.

## Best practices

- Let Dataverse enforce data security; don't re-implement it.
- Take the caller's identity only from the platform's validated principal.
- Return 404 for both "doesn't exist" and "not yours".
- Use user delegation SAS: short-lived, read-only, one blob.
- Isolate and review the impersonation privilege.

## Guided lab: see impersonation decide what a user can read

**Requires:** the host running locally, the sample lease `LSE-0042` and invoices from earlier labs, and your developer account with System Administrator in the development environment (which includes the impersonation privilege). Optional: a second test user with a restricted role.

### Lab 22.1 – No identity, no pack

```bash
curl -i http://localhost:7071/api/v1/leases/LSE-0042/pack
```

**Checkpoint:** `401 Unauthorized`. The function refuses to run without a caller identity.

### Lab 22.2 – Build a principal header for yourself

Find your object ID, and build the header that Easy Auth would send:

```bash
OID=$(az ad signed-in-user show --query id -o tsv)
PRINCIPAL=$(printf '{"claims":[{"typ":"http://schemas.microsoft.com/identity/claims/objectidentifier","val":"%s"}]}' "$OID" | base64 -w0)
```

(On macOS, use `base64` without `-w0`.)

### Lab 22.3 – Download your pack

```bash
curl -s -o lease-pack-LSE-0042.xlsx -w "%{http_code} %{size_download} bytes\n" \
  -H "X-MS-CLIENT-PRINCIPAL: $PRINCIPAL" \
  http://localhost:7071/api/v1/leases/LSE-0042/pack
```

**Checkpoint:** `200` and a small file. Open it: a **Lease** sheet with the lease, property, tenant, dates and rent, and an **Invoices** sheet listing the lease's invoices. Locally the app itself runs as you, so it's impersonating you as yourself; in Azure it's the managed identity impersonating the agent.

### Lab 22.4 – Ask for a lease that doesn't exist

```bash
curl -s -H "X-MS-CLIENT-PRINCIPAL: $PRINCIPAL" \
  http://localhost:7071/api/v1/leases/LSE-9999/pack
```

**Checkpoint:** a 404 problem with *Lease LSE-9999 was not found.* For a restricted user, a lease they can't read looks exactly the same, which is what you want: the API doesn't reveal that a lease exists.

### Lab 22.5 – A restricted user (optional)

Create a test user in your development tenant, add them to the environment with only a custom role that has **no** read access to `hl_lease`, and repeat Labs 22.2–22.3 with their object ID.

**Checkpoint:** a 404 for `LSE-0042`, although it exists. Now give the role user-level read on leases and assign one lease to that user: they can download that lease's pack and no other. The function didn't change; Dataverse's security did all the work.

### Lab 22.6 – Why Easy Auth matters

Look again at Lab 22.2: you just told the app to act as a user by **writing a header**. On your laptop nothing stops you, because Easy Auth isn't running. In Azure, with authentication required, the platform validates the user's token and replaces any `X-MS-CLIENT-PRINCIPAL` the caller sent.

**Checkpoint:** write down, in one sentence, what would happen if this function were deployed with authentication switched off. Then check that your deployed app has **Require authentication** set (Chapter 14's screen) before you ship it.

## Hands-on exercise

::: exercise
**Goal:** audit every pack download: log the caller's object ID, the lease number and the delivery method, and write a KQL query listing downloads per agent per day.
:::

## Knowledge check

::: knowledge-check
1. Where does the function get the caller's identity, and why can it trust it?
2. What are the effective privileges when impersonating?
3. Why does the function clone the `ServiceClient`?
4. Why does the API answer 404 rather than 403 for another branch's lease?
:::

Answers are in Appendix G.

::: summary
- App Service authentication identifies the agent; the function reads the validated principal.
- Impersonation applies the agent's own Dataverse security to the export.
- Small files return directly; large ones go out through short-lived user delegation SAS links.
- The impersonation privilege is powerful and must be isolated and reviewed.
:::

## References

- [Impersonate another user](https://learn.microsoft.com/power-apps/developer/data-platform/impersonate-another-user) – Microsoft Learn, checked 2026-10-27.
- [Work with user identities in Azure App Service authentication](https://learn.microsoft.com/azure/app-service/configure-authentication-user-identities) – Microsoft Learn, checked 2026-10-27.
- [Create a user delegation SAS with .NET](https://learn.microsoft.com/azure/storage/blobs/storage-blob-user-delegation-sas-create-dotnet) – Microsoft Learn, checked 2026-10-27.

<!-- Glossary terms introduced in this chapter: impersonation, client principal, user delegation SAS, on-behalf-of flow -->

<!-- expanded: deep dive + guided lab -->
