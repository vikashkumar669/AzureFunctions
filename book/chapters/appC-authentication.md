---
chapter: "C"
appendix: true
title: "Dataverse Authentication Reference"
---

## Which identity for which caller

::: {#tbl:whichid caption="Choosing how a caller authenticates to Dataverse"}
| Caller | Recommended | Alternative | Avoid |
|---|---|---|---|
| Azure Functions, App Service, Container Apps | Managed identity + application user | User-assigned managed identity | Client secrets |
| Dataverse plug-in calling Azure | Power Platform managed identity | — | Secrets in plug-in configuration |
| Pipeline deploying to Azure | OIDC / workload identity federation | — | Stored service principal secrets |
| Partner system outside Azure | Certificate credential | Partner's own managed identity via federation | Shared passwords |
| Developer laptop | Your own sign-in (`az login`) | Visual Studio sign-in | Shared service accounts |
| User-facing app reading as the user | User token (delegated) or impersonation | On-behalf-of flow | Application access with filtering in code |
:::

## Token scope

Request a token for `https://<org>.crm[N].dynamics.com/.default`, the environment URL's root plus `/.default`. A token for another environment or the wrong region fails with 401.

## Setting up an application user (summary)

1. Identify the identity's **application (client) ID** (Enterprise applications › filter *Managed Identities*).
2. Power Platform admin center › environment › Settings › Users + permissions › Application users › **New app user**.
3. Add the app by ID, choose the business unit, assign a **custom least-privilege role**.
4. For impersonation only: assign *Act on Behalf of Another User* directly, in its own role.

## Errors

::: {#tbl:autherrors caption="Authentication and authorization errors"}
| Error | Meaning | Look at |
|---|---|---|
| `DefaultAzureCredential failed to retrieve a token` | No credential source worked | Sign-in, tenant, managed identity enabled |
| `AADSTS500011` | Resource (scope) not found in the tenant | Environment URL in the scope |
| `AADSTS700016` | Application not found in directory | Client ID and tenant |
| `AADSTS7000215` | Invalid client secret | Prefer managed identity; rotate if unavoidable |
| `AADSTS70021` | No matching federated identity record | Federated credential subject |
| HTTP 401/403 from Dataverse with a valid token | No application user, or user disabled | Application users list |
| `PrivilegeDenied` `0x80040220` (-2147220960) | Role lacks the named privilege | Add exactly that privilege |
| `PrincipalPrivilegeDenied` `0x80040231` (-2147220943) | Target user or team lacks privileges | Roles of the target principal |
:::

Error codes and descriptions for Dataverse are listed on Microsoft Learn's "Web service error codes" page; Entra codes on "Microsoft Entra authentication and authorization error codes".
