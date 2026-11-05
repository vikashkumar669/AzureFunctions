# Azure Functions for Dynamics 365 & Power Platform — companion code

These docs accompany the book **Azure Functions for Dynamics 365 & Power Platform: A Practical Guide to Integration, Automation, APIs, and Enterprise Solutions** by Vikash Kumar.

The repository contains the working code for every example in the book: a .NET 10 isolated-worker Function App that integrates with Dataverse (Dynamics 365), plus sample tools, infrastructure templates, pipelines and unit tests.

> **Fictional scenario.** Harbourline Group, Harbourline Connect, LedgerOne and SignBridge are fictional names invented for the book. No real customer data or credentials are included.

## Pages

- **[Azure Function and connecting to D365](connecting-to-d365.md)** ([wiki](https://github.com/vikashkumar669/AzureFunctions/wiki/Azure-Function-and-connecting-to-D365)) — how the Function App connects to Dataverse, every function in the app, and a chapter-by-chapter map to the code.

## Quick start

1. Install the **.NET 10 SDK**, **Azure Functions Core Tools v4**, **Azurite** and the **Azure CLI**.
2. Clone the repo:
   ```bash
   git clone https://github.com/vikashkumar669/AzureFunctions.git
   cd AzureFunctions
   ```
3. Copy `src/Harbourline.Connect.Functions/local.settings.sample.json` to `local.settings.json` in the same folder, and set `Dataverse__Url` to your **developer** environment (never production).
4. Sign in with an account that has a security role in that environment:
   ```bash
   az login
   ```
5. (Optional) Create the sample tables and lab data in your dev environment:
   ```bash
   dotnet run --project samples/Harbourline.Setup -- --url https://<org>.crm.dynamics.com --sample-data
   ```
6. Start Azurite, then the Function App:
   ```bash
   cd src/Harbourline.Connect.Functions
   func start
   ```
7. Open <http://localhost:7071/api/health> — a healthy response means the app can reach Dataverse.

## Build and test

```bash
dotnet build HarbourlineConnect.slnx -c Release
dotnet test --solution HarbourlineConnect.slnx -c Release
```

GitHub Actions runs the same build and tests on every push to `main` (`.github/workflows/azure-functions-book.yml`). An Azure DevOps alternative is in `pipelines/azure-pipelines.yml`.

## Technology baseline

| Item | Version |
|---|---|
| .NET | 10 (LTS) |
| Azure Functions | Runtime 4.x, isolated worker model |
| Hosting | Flex Consumption plan |
| Dataverse SDK | `Microsoft.PowerPlatform.Dataverse.Client` |
| Tests | xUnit + NSubstitute |

Exact package versions are in each `.csproj`.

## Repository layout

```
src/
  Harbourline.Connect.Functions/   Function App (HTTP, timer, Service Bus, blob, Durable triggers)
  Harbourline.Connect.Core/        Business logic and Dataverse access; no Functions types
  Harbourline.Connect.Contracts/   Request/response DTOs shared with API callers
tests/
  Harbourline.Connect.UnitTests/   xUnit + NSubstitute
  requests/properties.http         Ready-made HTTP requests for the Properties API
samples/                           Setup tool, probe, partner mocks, ops tool, trigger gallery
infra/                             Bicep templates, API Management policy and KQL queries
pipelines/                         Azure DevOps pipeline
```

## Security notes

- `local.settings.json` is git-ignored — never commit it.
- The code authenticates with `DefaultAzureCredential`: your `az login` locally, the Function App's managed identity in Azure. No client secrets are needed for Dataverse.
- The keys in the sample settings file (`local-development-key`, `local-development-secret…`) only work against the local mocks.

## Licence

The code is MIT-licensed (see `LICENSE`). The book itself is not part of this repository; its text and figures are © Vikash Kumar, all rights reserved.
