---
chapter: 5
part: "Part II — Connecting to Dynamics 365 / Dataverse"
title: "Authentication and Secure Connectivity"
build-summary: "Harbourline Connect reaches Dataverse with a managed identity and a least-privilege role, keeps partner keys in Key Vault, and you can read the error when any link in that chain breaks."
builds:
  - "A secret-free Dataverse client factory built on DefaultAzureCredential"
  - "An application user with a custom Harbourline Integration security role"
  - "Key Vault references for the partner secrets that cannot be avoided"
versions: ".NET 10 · Azure.Identity 1.21.0 · Microsoft.PowerPlatform.Dataverse.Client 1.2.27"
---

::: objectives
- Explain how Microsoft Entra ID, an application user and a security role together decide what a function may do in Dataverse.
- Compare client secrets, certificates and managed identities, and justify managed identity for Harbourline.
- Build a Dataverse client that gets tokens from `DefaultAzureCredential`.
- Create an application user for a managed identity and give it a least-privilege custom role.
- Keep unavoidable secrets in Key Vault and reference them from app settings.
- Diagnose `AADSTS` and Dataverse permission errors from their messages.
:::

## The business problem

Harbourline's old integration signed in to Dynamics 365 as `integration@harbourline…`, a licensed user with the System Administrator role and a password in a configuration file. The password never expired, because changing it broke the integration. Three people knew it. One of them had left.

The security review set three rules for Harbourline Connect. No passwords or client secrets for Microsoft services. Each integration gets only the permissions it needs. And every access must be traceable to one identity.

## How it works

Three systems take part in every call from a function to Dataverse (@fig:identity).

**Microsoft Entra ID** proves who is calling. It issues an **access token**: a signed statement that a given identity may call a given resource, valid for a limited time. For Dataverse, the token is requested for the scope `https://<org>.crm.dynamics.com/.default`.

**Dataverse** decides what that identity may do. It accepts a token from an identity in its tenant only if the identity exists in the environment as a user. For software, that is an **application user**: a user record tied to an Entra ID application, with no licence and no interactive sign-in.

The application user's **security roles** grant privileges: create, read, write, delete and others, per table, at a depth from the user's own rows up to the whole organization. The token gets the function through the door; the role decides which rooms it may enter.

![Locally your developer sign-in, in Azure the managed identity: the same code gets a token either way, and Dataverse decides what it may do.](figures/ch05/identity.svg){#fig:identity}

### Three ways for software to prove its identity

An Entra ID application can prove itself in three ways (@tbl:creds).

::: {#tbl:creds caption="Credentials for service-to-service authentication"}
| Credential | What you store | Rotation | Risk if leaked | Harbourline use |
|---|---|---|---|---|
| Client secret | A password string | Manual, before expiry | Anyone can sign in as the app | Never for Microsoft services |
| Certificate | A private key | Manual or automated | Anyone with the key can sign in | Only where managed identity is impossible |
| **Managed identity** | Nothing | Automatic, by Azure | No credential exists to leak | Every Azure-hosted component |
:::

A **managed identity** is an Entra ID identity that Azure creates for one resource and manages for it. A *system-assigned* identity lives and dies with its Function App. A *user-assigned* identity is a separate resource that several apps can share, useful when an identity must survive the app being recreated, or must be granted access before the app exists.

### One credential for laptop and cloud

`DefaultAzureCredential` from Azure.Identity tries a sequence of credential types and uses the first that works. In Azure, the managed identity answers. On a laptop, the managed identity endpoint doesn't exist, so it moves on to developer sign-ins such as Visual Studio and the Azure CLI. One line of code, two environments, no secret in either.

That convenience has a cost: when nothing works, it tries every option before failing, and a mis-configured environment can pick up an unexpected sign-in. Chapter 25 discusses pinning the credential type in production.

## Architecture

::: {#tbl:components caption="Components in @fig:identity"}
| Component | Role | Authenticates with | Failure path |
|---|---|---|---|
| Function App | Calls Dataverse | System-assigned managed identity | `AuthenticationFailedException` from Azure.Identity |
| Microsoft Entra ID | Issues tokens | — | `AADSTS` error codes |
| Dataverse application user | Maps the identity to a Dataverse user | Token from Entra ID | 401/403 if missing or disabled |
| Harbourline Integration role | Grants table privileges | — | `PrivilegeDenied` (0x80040220) |
| Key Vault | Holds partner secrets | Managed identity, *Key Vault Secrets User* role | App setting shows an unresolved reference |
:::

## Prerequisites

- Chapters 3 and 4: tools installed, schema created in your developer environment.
- To create an application user you need the System Administrator role in the environment.
- To look up a managed identity's application ID you need read access to Microsoft Entra ID.

## Step-by-step implementation

### Step 1 – Build a client that asks a credential for tokens

`ServiceClient` usually takes a connection string, but it also accepts a function that returns an access token whenever it needs one. @lst:factory passes it a function that asks an Azure `TokenCredential`.

```{.csharp #lst:factory include="src/Harbourline.Connect.Core/Dataverse/DataverseClientFactory.cs" region="dataverse-factory" caption="A ServiceClient that gets its tokens from an Azure credential"}
```

1. The factory depends on the abstract `TokenCredential`, so tests can pass a fake and production passes `DefaultAzureCredential`.
2. The scope is the environment's root URL plus `/.default`: "all the permissions this app has been given on this resource".
3. `ServiceClient` calls this function when it needs a token. Azure.Identity caches tokens and refreshes them before they expire, so this is cheap.
4. `useUniqueInstance: true` gives this client its own connection rather than sharing one cached by URL.

Nothing in the factory names a tenant, a client ID or a secret. Who the code runs as is decided entirely by the environment.

### Step 2 – Turn on the Function App's identity

In the Azure portal, open the Function App, select **Settings › Identity**, set **System assigned** to **On**, and save. The Bicep template in Chapter 20 does the same with `identity: { type: 'SystemAssigned' }`.

Note the **Object (principal) ID**. Then find the identity's **application ID** (also called client ID), which Dataverse needs: in the Microsoft Entra admin center, open **Enterprise applications**, change the application type filter to **Managed Identities**, and select the identity, which has the Function App's name.

### Step 3 – Create the application user

In the Power Platform admin center, open the environment, then **Settings › Users + permissions › Application users**, and select **New app user**. Choose **Add an app**, find the application ID from Step 2, pick the business unit, and add the **Harbourline Integration** role (Step 4).

::: note
**If your screen differs:** the admin center is updated often. The same page is reachable from the environment's **Settings** command; search the page for "Application users".
:::

### Step 4 – Create a least-privilege role

Copy no existing role. Start from an empty one, named *Harbourline Integration*, and add only what Harbourline Connect does (@tbl:role).

::: {#tbl:role caption="Privileges of the Harbourline Integration role"}
| Table | Create | Read | Write | Delete | Depth | Used by |
|---|---|---|---|---|---|---|
| Property (`hl_property`) | ✓ | ✓ | ✓ | – | Organization | Property API, feeds, migration |
| Lease (`hl_lease`) | – | ✓ | ✓ | – | Organization | Signing workflow |
| Invoice (`hl_invoice`) | ✓ | ✓ | ✓ | – | Organization | Invoice sync |
| Service order (`hl_serviceorder`) | ✓ | ✓ | ✓ | – | Organization | Order intake |
| Meter reading (`hl_meterreading`) | ✓ | ✓ | ✓ | – | Organization | Bulk load |
| Sync state (`hl_syncstate`) | ✓ | ✓ | ✓ | – | Organization | Checkpoints |
| Account, Contact | – | ✓ | ✓ | – | Organization | Customer sync, signing |
:::

No delete privilege anywhere: no integration in this book deletes rows, so none can delete one by mistake. Chapter 22 adds one more privilege, *Act on Behalf of Another User*, and explains why it is granted on its own.

### Step 5 – Keep unavoidable secrets in Key Vault

LedgerOne, the fictional finance system, only accepts an API key. That secret can't be avoided, but it can be kept out of code and app settings. Store it in Key Vault and point the app setting at it with a **Key Vault reference**:

```text
LedgerOne__ApiKey = @Microsoft.KeyVault(VaultName=kv-hl-dev-…;SecretName=ledgerone-api-key)
```

The platform resolves the reference with the Function App's managed identity, which needs the *Key Vault Secrets User* role on the vault. Your code reads `LedgerOne:ApiKey` from configuration as usual and never knows a vault was involved. Rotating the key means adding a new secret version; the platform picks it up without a deployment.

::: security
Grant *Key Vault Secrets User*, which can only read secret values, never *Key Vault Administrator*. Use Azure role-based access control on the vault rather than access policies, so vault permissions are managed like every other Azure permission.
:::

### Step 6 – Prove it end to end

Locally, `DefaultAzureCredential` uses your `az login`, so your own user must exist in the developer environment with a role that can read `hl_property`. Run the probe from Chapter 4: if it lists properties, the token chain works.

In Azure, after deploying (Chapter 6), call the Property API. A 200 response proves that the managed identity, the application user and the role are all in place.

## Screens you will use

::: {.capture #fig:identity-blade caption="Turning on the Function App's system-assigned identity." app="Azure portal" path="Function App › Settings › Identity › System assigned" annotations="1 Status: On; 2 Save; 3 Object (principal) ID"}
:::

::: {.capture #fig:app-user caption="Adding the managed identity as an application user with the Harbourline Integration role." app="Power Platform admin center" path="Environments › Harbourline-Dev › Settings › Users + permissions › Application users › New app user" annotations="1 Add an app; 2 Business unit; 3 Security roles: Harbourline Integration; 4 Create"}
:::

::: {.capture #fig:role caption="The custom role grants Organization-level privileges on Harbourline tables only." app="Power Platform admin center" path="Settings › Users + permissions › Security roles › Harbourline Integration" annotations="1 Custom tables tab; 2 Property row; 3 Read: Organization; 4 Delete: None"}
:::

**Expected result:** the application user appears in the list with status *Active*, and the role shows no delete privileges.

## Complete working code

- `src/Harbourline.Connect.Core/Dataverse/DataverseClientFactory.cs`: the factory.
- `src/Harbourline.Connect.Core/ServiceCollectionExtensions.cs`: registers `DefaultAzureCredential` as the app's `TokenCredential` (Chapter 6).
- `infra/main.bicep`: the identity, the Key Vault and its role assignment (Chapter 20).

## Configuration

::: {#tbl:settings caption="Identity-related settings"}
| Setting | Local | Azure | Secret? |
|---|---|---|---|
| `Dataverse__Url` | Developer environment URL | Per environment | No |
| `LedgerOne__ApiKey` | A local value accepted by the mocks | Key Vault reference | Yes, in Key Vault only |
| `AZURE_CLIENT_ID` | Not set | Only for a user-assigned identity | No |
:::

## Sample input and output

A decoded access token (header and signature omitted; values fictional) shows what Dataverse receives:

```json
{
  "aud": "https://harbourline-dev.crm11.dynamics.com",
  "iss": "https://sts.windows.net/00000000-0000-0000-0000-000000000000/",
  "appid": "11111111-2222-3333-4444-555555555555",
  "idtyp": "app",
  "exp": 1793123456
}
```

`aud` must be your environment; `appid` must match the application user's application ID. When access fails, those two claims are the first things to check.

## Testing

Authentication is tested end to end, not with mocks: the probe locally and a smoke test after each deployment (Chapter 20) are the checks that matter. The unit tests cover the part that can be tested in isolation: `DataverseOptionsTests` proves that a missing or malformed `Dataverse__Url` fails validation, and `ValidateOnStart` turns that into a start-up failure in Azure rather than a 500 on the first request.

## Common errors and troubleshooting

::: {#tbl:errors caption="Authentication errors and their usual causes"}
| Symptom | Likely cause | Fix |
|---|---|---|
| `DefaultAzureCredential failed to retrieve a token` locally | Not signed in, or signed in to another tenant | `az login --tenant <tenant-id>` |
| `AADSTS500011`: resource principal not found | Scope URL wrong (typo or wrong region) | Copy the URL from the admin center |
| `AADSTS700016`: application not found in the directory | Wrong tenant or client ID | Check `AZURE_TENANT_ID`, `AZURE_CLIENT_ID` |
| HTTP 401/403 from Dataverse with a valid token | No application user for this identity | Create it (Step 3) |
| `PrivilegeDenied` (0x80040220) naming a privilege | Role lacks that privilege | Add exactly that privilege to the role |
| App setting value starts with `@Microsoft.KeyVault(` at run time | Reference not resolved | Grant *Key Vault Secrets User*; check the secret name |
:::

The `PrivilegeDenied` message names the missing privilege, for example `prvReadhl_property`. Read it: it tells you exactly which box to tick in the role.

## Security considerations

::: security
Never assign System Administrator or System Customizer to an application user. A compromised integration with those roles can read every row and change the schema. With the Harbourline Integration role, the same compromise reaches six tables and can delete nothing.
:::

Two more rules. Give each Function App its own identity, so that permissions and audit trails stay separate. And review application users quarterly: an identity whose Function App no longer exists should be disabled.

## Performance and scalability

::: performance
Token requests are not free, but they are rare: Azure.Identity caches tokens and refreshes them shortly before expiry. Register one credential and one `ServiceClient` per app (singletons, Chapter 6) so that cache is shared.
:::

## Alternatives and design decisions

When managed identity is impossible, for example a partner's server outside Azure calling Dataverse, use a certificate rather than a secret, and store its private key in that system's own secure store. For code running *inside* Dataverse, plug-ins can now use a Power Platform managed identity, generally available, to call Azure resources such as Key Vault without credentials.

A user-assigned identity is the better choice when several apps must share permissions, or when you need to grant access before the app exists, as in a pipeline that creates the application user first. Harbourline uses system-assigned identities: one app, one identity, nothing to clean up.

## Best practices

- Use managed identities for every Azure-to-Microsoft-service call.
- One identity per Function App; one custom role per integration.
- Grant only the privileges the code uses; grant no delete unless code deletes.
- Keep unavoidable third-party secrets in Key Vault, referenced from app settings.
- Read the error: `AADSTS` codes point to Entra ID, `PrivilegeDenied` to the role.
- Review application users and their roles every quarter.

## Hands-on exercise

::: exercise
**Goal:** break the chain on purpose and read each error.

**Steps:** with the app deployed (Chapter 6), remove the Harbourline Integration role from the application user and call the API; restore it. Then change `Dataverse__Url` to a typo and restart; restore it.

**Done when:** you can name, for each failure, which link broke and which message told you.

**Stretch:** replace the system-assigned identity with a user-assigned identity. Which setting must you add, and why?
:::

## Knowledge check

::: knowledge-check
1. What does the access token prove, and what decides what the function may do?
2. Why is a managed identity safer than a client secret?
3. How does `DefaultAzureCredential` work on a laptop and in Azure with the same code?
4. Which two IDs of a managed identity will you need, and where is each used?
5. A call fails with `PrivilegeDenied` naming `prvWritehl_lease`. What do you change?
:::

Answers are in Appendix G.

::: summary
- Entra ID authenticates; Dataverse authorizes through an application user and its roles.
- Managed identities remove secrets for Azure-hosted code; there is nothing to leak or rotate.
- `DefaultAzureCredential` lets the same code use your sign-in locally and the managed identity in Azure.
- A custom, least-privilege role limits the damage any compromise can do.
- Third-party secrets live in Key Vault, behind Key Vault references.
:::

## References

- [Use managed identities for App Service and Azure Functions](https://learn.microsoft.com/azure/app-service/overview-managed-identity) – Microsoft Learn, checked 2026-10-27.
- [Manage application users in the Power Platform admin center](https://learn.microsoft.com/power-platform/admin/manage-application-users) – Microsoft Learn, checked 2026-10-27.
- [Security roles and privileges](https://learn.microsoft.com/power-platform/admin/security-roles-privileges) – Microsoft Learn, checked 2026-10-27.
- [Authentication with .NET and DefaultAzureCredential](https://learn.microsoft.com/dotnet/azure/sdk/authentication/credential-chains) – Microsoft Learn, checked 2026-10-27.
- [Use Key Vault references for App Service and Azure Functions](https://learn.microsoft.com/azure/app-service/app-service-key-vault-references) – Microsoft Learn, checked 2026-10-27.
- [Set up managed identity for Power Platform](https://learn.microsoft.com/power-platform/admin/set-up-managed-identity) – Microsoft Learn, checked 2026-10-27.
- [Microsoft Entra authentication and authorization error codes](https://learn.microsoft.com/entra/identity-platform/reference-error-codes) – Microsoft Learn, checked 2026-10-27.

<!-- Glossary terms introduced in this chapter: access token, application user, security role, privilege depth, managed identity, system-assigned identity, user-assigned identity, DefaultAzureCredential, Key Vault reference -->
