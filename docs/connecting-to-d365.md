# Azure Function and connecting to D365

This page explains how the Function App talks to Dynamics 365 (Dataverse), lists every function in the app, and maps each chapter of the book to the code that goes with it.

## How the connection works

The app uses the Dataverse SDK (`Microsoft.PowerPlatform.Dataverse.Client`) and gets its access tokens from an Azure `TokenCredential` instead of a client secret.

- **Locally**, `DefaultAzureCredential` uses your `az login`.
- **In Azure**, it uses the Function App's **managed identity**, which must be added as an application user with a security role in the Dataverse environment.

The client is created once in `DataverseClientFactory`:

```csharp
var instanceUrl = new Uri(options.Url);
string[] scopes = [instanceUrl.GetLeftPart(UriPartial.Authority) + "/.default"];

return new ServiceClient(
    instanceUrl,
    async _ => (await credential.GetTokenAsync(
        new TokenRequestContext(scopes), CancellationToken.None)).Token,
    useUniqueInstance: true,
    logger: logger);
```

| File | Purpose |
|---|---|
| `src/Harbourline.Connect.Core/Dataverse/DataverseClientFactory.cs` | Builds the `ServiceClient` from a token credential |
| `src/Harbourline.Connect.Core/Dataverse/DataverseOptions.cs` | Binds the `Dataverse` settings section (`Dataverse__Url`, `Dataverse__OrganizationId`) |
| `src/Harbourline.Connect.Core/Dataverse/UserScope.cs` | Runs a call on behalf of the calling user instead of the app identity |
| `src/Harbourline.Connect.Core/Dataverse/DataverseErrors.cs` | Translates Dataverse faults (throttling, duplicates, not found) into API outcomes |
| `src/Harbourline.Connect.Core/Dataverse/Schema.cs` | Table and column logical names in one place |
| `src/Harbourline.Connect.Core/ServiceCollectionExtensions.cs` | Registers the client and services for dependency injection |

### Checklist for a new environment

1. Create a **developer** environment in the Power Platform admin center.
2. Run the setup tool to create the sample tables (add `--sample-data` for lab rows):
   ```bash
   dotnet run --project samples/Harbourline.Setup -- --url https://<org>.crm.dynamics.com
   ```
3. Set `Dataverse__Url` (and `Dataverse__OrganizationId`) in `local.settings.json` or in the Function App's settings.
4. In Azure, enable the Function App's managed identity and add it as an **application user** in the environment with a suitable security role.
5. Call `GET /api/health` to confirm the connection.

## Functions in the app

### HTTP APIs

| Function | Method & route | What it does |
|---|---|---|
| `Health` | `GET /api/health` | Checks the app and its Dataverse connection (anonymous) |
| `ListProperties` | `GET /api/v1/properties` | Lists properties with paging (continuation tokens) |
| `GetProperty` | `GET /api/v1/properties/{code}` | Reads one property |
| `CreateProperty` | `POST /api/v1/properties` | Validates and creates a property |
| `UpdateProperty` | `PATCH /api/v1/properties/{code}` | Updates a property |
| `SearchProperties` | `GET /api/v1/search/properties` | Searches properties |
| `SubmitOrder` | `POST /api/v1/orders` | Accepts an order and queues it on Service Bus |
| `GetOrderStatus` | `GET /api/v1/orders/{number}` | Returns an order's processing status |
| `ExportInvoices` | `GET /api/v1/finance/invoices/{month}` | Streams a month's invoices as an Excel workbook |
| `ImportPrices` | `POST /api/v1/finance/prices` | Imports a price list from an Excel workbook |
| `GetLeasePack` | `GET /api/v1/leases/{number}/pack` | Starts a Durable orchestration that builds a lease pack |
| `LeaseStatusWebhook` | `POST /api/webhooks/lease-status` | Receives lease status events |
| `SignBridgeCallback` | `POST /api/callbacks/signbridge` | Receives signed callbacks from the e-signature partner (signature verified) |

Ready-made requests for the Properties API are in `tests/requests/properties.http`.

### Background functions

| Function | Trigger | What it does |
|---|---|---|
| `NightlyInvoiceSync` | Timer (`InvoiceSyncSchedule`, default 02:00) | Syncs changed invoices from Dataverse to the finance system |
| `PushCustomersToLedgerOne` | Timer | Pushes customer changes to LedgerOne |
| `PullCustomersFromLedgerOne` | Timer | Pulls customer changes back, resolving conflicts |
| `ProcessOrder` | Service Bus | Processes queued orders into Dataverse |
| `AccountEventsToLedgerOne` | Service Bus | Forwards Dataverse account events to LedgerOne |
| `StartLeaseSigning` | Service Bus | Sends a lease to SignBridge for signing |
| `ImportPropertyFeed` | Blob | Imports a property feed file dropped into storage |
| `LoadMeterReadings` | Blob | Bulk-loads meter readings in batches (`Bulk__BatchSize`) |

## Sample tools

| Project | Use it to |
|---|---|
| `samples/Harbourline.Setup` | Create the sample schema and optional lab data in a dev environment |
| `samples/Harbourline.Probe` | Test connectivity and authentication to Dataverse |
| `samples/Harbourline.Mocks` | Run local mocks of the LedgerOne and SignBridge partner APIs (port 5080) |
| `samples/Harbourline.Ops` | Inspect and replay Service Bus dead-letter messages |
| `samples/Harbourline.Samples.Triggers` | Try a small gallery of trigger types on their own |

## Chapter-to-code map

| # | Chapter | Where to look |
|---|---|---|
| 1 | Introduction | `README.md` |
| 2 | Architecture | `src/` project layout |
| 3 | Environment setup | `samples/Harbourline.Setup`, `local.settings.sample.json` |
| 4 | Dataverse APIs | `Core/Dataverse/` |
| 5 | Authentication | `DataverseClientFactory.cs`, `UserScope.cs`, `Http/ClientPrincipal.cs`, `samples/Harbourline.Probe` |
| 6 | Your first function | `Functions/HealthFunction.cs`, `Core/Health/` |
| 7 | CRUD APIs | `Http/PropertyFunctions.cs`, `Core/Properties/` |
| 8 | External APIs | `Core/Integrations/` (LedgerOne, SignBridge), `samples/Harbourline.Mocks` |
| 9 | Webhooks | `Webhooks/LeaseWebhookFunction.cs`, `Leases/SignBridgeCallbackFunction.cs`, `CallbackSignature.cs` |
| 10 | Scheduled jobs | `Jobs/InvoiceSyncFunction.cs`, `Core/Sync/` |
| 11 | Service Bus | `Messaging/AccountEventsFunction.cs`, `Leases/LeaseSigning.cs`, `samples/Harbourline.Ops` |
| 12 | Durable Functions | `Exports/LeasePackFunction.cs`, `Core/Exports/` |
| 13 | File processing | `Feeds/PropertyFeedFunction.cs`, `Core/Feeds/` |
| 14 | Securing the API | `infra/apim.bicep`, `infra/apim/`, `Http/ClientPrincipal.cs` |
| 15 | High-volume processing | `Bulk/MeterReadingLoadFunction.cs`, `Core/Bulk/` |
| 16 | Data migration | `Core/Migration/` |
| 17 | Working with Excel | `Excel/FinanceExcelFunctions.cs`, `Core/Excel/` |
| 18 | Monitoring | `Middleware/CorrelationMiddleware.cs`, `Core/Telemetry/`, `infra/monitoring/queries.kql` |
| 19 | Testing | `tests/Harbourline.Connect.UnitTests/` |
| 20 | DevOps | `.github/workflows/`, `pipelines/azure-pipelines.yml`, `infra/main.bicep` |
| 21 | Case study: customer sync | `Jobs/CustomerSyncFunctions.cs`, `Core/Customers/` |
| 22 | Case study: Excel export API | `Core/Excel/FinanceService.cs`, `StreamingWorkbook.cs` |
| 23 | Case study: search API | `Search/PropertySearchFunction.cs`, `Core/Search/` |
| 24 | Case study: order processing | `Orders/OrderFunctions.cs`, `Core/Orders/` |
| 25 | Production readiness | `host.json`, `infra/`, `infra/monitoring/` |

Paths without a top-level folder are relative to `src/Harbourline.Connect.Functions/` or `src/Harbourline.Connect.Core/` (`Core/…`).
