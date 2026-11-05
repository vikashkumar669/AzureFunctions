# Azure Functions for Dynamics 365 & Power Platform — companion code

Companion repository for the book **Azure Functions for Dynamics 365 & Power Platform: A Practical Guide to Integration, Automation, APIs, and Enterprise Solutions** by Vikash Kumar (self-published).

> **Fictional scenario.** Harbourline Group, Harbourline Connect, LedgerOne and SignBridge are fictional names invented for this book. Any resemblance to real companies, products or people is coincidental. No real customer data or credentials are included anywhere in this repository.

## Technology baseline

| Item | Version |
| --- | --- |
| .NET | 10 (LTS) |
| Azure Functions | Runtime 4.x, isolated worker model, `Azure.Functions.Sdk` |
| Hosting | Flex Consumption plan |
| Dataverse SDK | `Microsoft.PowerPlatform.Dataverse.Client` 1.2.27 |
| Tests | xUnit + NSubstitute (separate test project) |

Exact package versions are in each `.csproj`. The book's version statement lists the date they were last verified.

## Layout

```
AzureFunctions/
  HarbourlineConnect.slnx
  src/
    Harbourline.Connect.Functions/   Function App (HTTP, timer, Service Bus, … triggers)
    Harbourline.Connect.Core/        Business logic and Dataverse access; no Functions types
    Harbourline.Connect.Contracts/   Request/response DTOs shared with API callers
  tests/
    Harbourline.Connect.UnitTests/   xUnit + NSubstitute
  pipelines/                         Azure DevOps pipeline (alternative to GitHub Actions)
  book/                              Manuscript sources and the screenshot capture plan
```

Projects are added chapter by chapter; each chapter's finished state is tagged `ch-NN`.

## Run locally

1. Install the .NET 10 SDK, Azure Functions Core Tools v4, Azurite and the Azure CLI.
2. Copy `src/Harbourline.Connect.Functions/local.settings.sample.json` to `local.settings.json` in the same folder and set `Dataverse__Url` to your **developer** environment.
3. Run `az login` with an account that has a security role in that environment.
4. From `src/Harbourline.Connect.Functions`, run `func start` and open `http://localhost:7071/api/health`.

`local.settings.json` is git-ignored. Never commit secrets; the code authenticates with `DefaultAzureCredential` (your `az login` locally, the Function App's managed identity in Azure).

## Build and test

```bash
dotnet build HarbourlineConnect.slnx -c Release
dotnet test HarbourlineConnect.slnx -c Release
```

GitHub Actions runs the same steps on every push that touches this folder (`.github/workflows/azure-functions-book.yml` at the repository root).

## Licence

Code: MIT (see `LICENSE`). Book text and figures: © Vikash Kumar. All rights reserved.
