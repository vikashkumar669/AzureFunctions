---
chapter: "K"
appendix: true
title: "Lab Environment: Setup, Reset and Teardown"
---

The guided labs build on each other. This appendix collects, in one place, what each lab needs, the order in which to set things up, the permissions your account needs, how to reset between attempts, and how to remove everything afterwards. All names and data in the labs are fictional; never run them against production.

## What each lab needs

::: {#tbl:labneeds caption="Requirements of the guided labs"}
| Labs | Laptop only | Dataverse dev environment | Azure subscription | Also needs |
|---|---|---|---|---|
| 1, 2, 3, 19 | ✓ | — | — | — |
| 4, 5, 6, 7 | ✓ | ✓ | Optional (deployment steps) | Azure CLI sign-in |
| 8 | ✓ | Lab 8.4 only | — | — |
| 9 | ✓ | ✓ | Service Bus (Lab 9.5) | — |
| 10, 16, 17, 21, 22, 23 | ✓ | ✓ | — | Mocks running |
| 11, 12, 24 | ✓ | ✓ | Service Bus | Durable Task Scheduler emulator (12) |
| 13, 15 | — | ✓ | Function App, storage, Event Grid | — |
| 14 | — | ✓ | API Management, Function App | Entra ID rights to create app registrations |
| 18, 20, 25 | — | ✓ | Full environment from Chapter 20 | GitHub fork (20) |
:::

Labs marked *laptop only* use the emulators and mocks and cost nothing beyond your own machine.

## One-time setup, in order

1. **Tools** (Chapter 3): .NET 10 SDK, Azure Functions Core Tools 4, Azure CLI, Azurite, Docker (for the Durable Task Scheduler emulator), Git, `curl` and `jq`.
2. **Repository:** clone or fork `github.com/vikashkumar669/AzureFunctions`, then build and run the tests (Lab 1.1, Lab 1.2).
3. **Power Platform developer environment** (Chapter 3). Note its URL and organization ID.
4. **Schema** (Chapter 4): run the setup tool, publish customizations, and wait until all alternate keys are *Active*.
5. **Sample data** (Lab 4.0): run the setup tool again with `--sample-data`.
6. **Local settings** (Lab 3.4): copy `local.settings.sample.json` to `local.settings.json` and fill in your environment.
7. **Change tracking** on `account` (for Labs 10.7 and 21): in the maker portal, open the table's properties and turn on *Track changes*.
8. **Azure development resources**, when you reach the labs that need them: either follow the chapter steps for single resources (Service Bus in Chapter 11, for example) or deploy everything at once with the Bicep template and workflow from Chapter 20.
9. **Application user** for the Function App's managed identity, with the *Harbourline Integration* role (Chapter 5). For Chapter 22, also the *Harbourline Impersonation* role.
10. **Service Bus endpoint and webhook registrations** (Chapters 9 and 11) with the Plug-in Registration Tool, pointing at your development resources.

## Permissions your account needs

::: {#tbl:labroles caption="Permissions for running all labs"}
| Where | Role or permission | Used for |
|---|---|---|
| Dataverse dev environment | System Administrator | Setup tool, registrations, impersonation labs |
| Azure resource group | Contributor, plus Role Based Access Control Administrator (or User Access Administrator) | Deploying Bicep, which creates role assignments |
| Service Bus namespace | Azure Service Bus Data Owner | Service Bus Explorer, the Ops tool, local runs |
| Storage account | Storage Blob Data Contributor | Uploading feeds and readings with `--auth-mode login` |
| Durable Task Scheduler (Azure) | Durable Task Data Contributor | Running orchestrations locally against the cloud scheduler |
| Application Insights / Log Analytics | Reader (Log Analytics Reader for queries) | Telemetry labs |
| Microsoft Entra ID | Application Developer, or rights to create app registrations; an administrator to grant app roles | Labs 14 and 20 |
:::

Role assignments in Azure can take a few minutes to take effect. If a command fails with an authorization error right after you've granted a role, wait and try again before investigating further.

## Running the local stack

Most labs use four terminals:

```bash
# 1. Storage emulator
azurite --silent --location ./.azurite

# 2. Durable Task Scheduler emulator (labs 12 and 24)
docker run --rm -p 8080:8080 -p 8082:8082 mcr.microsoft.com/dts/dts-emulator:latest

# 3. Partner mocks (LedgerOne and SignBridge); add --FailureRate=<0..1> to inject failures
dotnet run --project samples/Harbourline.Mocks

# 4. The Functions host
cd src/Harbourline.Connect.Functions && func start
```

A helper to run any non-HTTP function on demand, used throughout the labs:

```bash
run() { curl -s -o /dev/null -w "$1: %{http_code}\n" -X POST \
  http://localhost:7071/admin/functions/$1 \
  -H "Content-Type: application/json" -d '{"input":""}'; }
```

## Resetting between attempts

::: {#tbl:labreset caption="Resetting state between lab attempts"}
| To reset | Do this |
|---|---|
| A sync or migration checkpoint | Delete its row in the **Sync states** table (`hl_syncstate`) |
| The mocks' data | Stop and restart the mocks; they keep everything in memory |
| Durable orchestrations (local) | Restart the emulator container, or purge instances in its dashboard |
| A Service Bus queue or subscription | Receive and complete the messages in Service Bus Explorer, including the dead-letter queue |
| Azurite | Stop it and delete the `.azurite` folder |
| Sample data | Run the setup tool with `--sample-data` again; it upserts the original values |
| Lab rows (codes `HBR-5000` and above, `ORD-…` orders) | A bulk delete job in the Power Platform admin center, filtered by code or number |
:::

## Keeping costs down

The laptop-only labs cost nothing. In Azure, a few resources cost money even when idle or lightly used, and should be removed when you finish a block of labs:

- **Service Bus Standard** has a base charge in addition to its per-operation charges.
- **Always-ready instances** on Flex Consumption are billed while they wait (Lab 2.4 shows how to remove them).
- **Log Analytics** charges for ingested data; debug-level logging in a lab can ingest a surprising amount.
- **API Management Consumption** charges per call; the rate-limit lab makes only a few dozen calls, but load tests make many more.
- **Azure Load Testing**, **Azure Managed Redis** and any other resource you add for a stretch exercise.

Check current prices with the Azure pricing calculator and set a budget with alerts on the lab resource group (Lab 25.5) before you start.

## Teardown

When you've finished, remove everything the labs created:

```bash
# Azure resources (everything in the lab resource group)
az group delete -n <resource-group> --yes --no-wait

# Key Vault names stay reserved while soft-deleted
az keyvault list-deleted --query "[].name" -o tsv
az keyvault purge -n <vault-name>

# Entra ID app registrations created in labs 14 and 20
az ad app delete --id <client-id-of-northwind-lettings-test>
az ad app delete --id <client-id-of-hlconnect-deploy-lab>
```

In the Power Platform admin center, either reset or delete the developer environment, or remove the lab rows with bulk delete jobs. Finally, delete `local.settings.json` and the `.azurite` folder from your clone, and sign out of the Azure CLI on shared machines (`az logout`).

## When a lab doesn't behave as described

::: {#tbl:labtrouble caption="Common lab problems"}
| Symptom | Likely cause | What to do |
|---|---|---|
| Setup tool fails creating sample data with a key error | Alternate keys not yet active | Wait until every key shows *Active*, then run again |
| A lab's expected output differs in wording | Tool or library version differs from the book's | Compare meaning, not exact text; the labs mark illustrative output |
| `401` from Dataverse in `curl` labs | The token from `az account get-access-token` expired | Get a new token |
| Admin endpoint returns `401` locally | Calling the deployed app instead of `localhost` | Locally no key is needed; in Azure send the master key in `x-functions-key` |
| Service Bus functions never fire locally | `ServiceBus__fullyQualifiedNamespace` not set, or your user lacks a data role | Set the namespace and assign *Azure Service Bus Data Owner* |
| Blob-triggered functions never fire locally | The Event Grid source needs a reachable endpoint | Run those labs against the deployed app (Labs 13 and 15) |
| Invoice export is empty | The mock's invoices are due in October 2026 | Export `2026-10`, or change the mock's sample data |
:::

If a lab still disagrees with what you see, check the companion repository's issues and README, which are updated when Microsoft changes a portal screen or a default.
