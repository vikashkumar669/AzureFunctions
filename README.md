# Azure Functions for Dynamics 365 & Power Platform — companion code

Companion repository for the book **Azure Functions for Dynamics 365 & Power Platform: A Practical Guide to Integration, Automation, APIs, and Enterprise Solutions**.

> **Fictional scenario.** Harbourline Group, Harbourline Connect, LedgerOne and SignBridge are fictional names invented for this book. Any resemblance to real companies, products or people is coincidental. No real customer data or credentials are included anywhere in this repository.

## Documentation

The **[project wiki](https://github.com/vikashkumar669/AzureFunctions/wiki)** has the full guide:

- **[Home](https://github.com/vikashkumar669/AzureFunctions/wiki)** — quick start, build and test, repository layout and security notes
- **[Azure Function and connecting to D365](https://github.com/vikashkumar669/AzureFunctions/wiki/Azure-Function-and-connecting-to-D365)** — how the Function App connects to Dataverse, every function in the app, the sample tools, and a chapter-by-chapter map to the code

The same pages are kept in [`docs/`](docs/README.md) so they travel with the code.

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
    requests/properties.http         Ready-made HTTP requests for the Properties API
  samples/                           Setup tool, probe, partner mocks, ops tool, trigger gallery
  infra/                             Bicep templates, API Management policy and KQL queries
  pipelines/                         Azure DevOps pipeline (alternative to GitHub Actions)
  docs/                              Quick start, D365 connection guide, function list, chapter map
```

## Run locally

1. Install the .NET 10 SDK, Azure Functions Core Tools v4, Azurite and the Azure CLI.
2. Copy `src/Harbourline.Connect.Functions/local.settings.sample.json` to `local.settings.json` in the same folder and set `Dataverse__Url` to your **developer** environment.
3. Run `az login` with an account that has a security role in that environment.
4. (Optional) Create the sample tables and lab data: `dotnet run --project samples/Harbourline.Setup -- --url https://<org>.crm.dynamics.com --sample-data`.
5. Start Azurite, then from `src/Harbourline.Connect.Functions`, run `func start` and open `http://localhost:7071/api/health`.

`local.settings.json` is git-ignored. Never commit secrets; the code authenticates with `DefaultAzureCredential` (your `az login` locally, the Function App's managed identity in Azure).

## Build and test

```bash
dotnet build HarbourlineConnect.slnx -c Release
dotnet test --solution HarbourlineConnect.slnx -c Release
```

GitHub Actions runs the same steps on every push to `main` and on pull requests (`.github/workflows/azure-functions-book.yml`). An Azure DevOps alternative is in `pipelines/azure-pipelines.yml`.

## Licence

The code in this repository is MIT-licensed (see `LICENSE`). It is the companion code for the book *Azure Functions for Dynamics 365 & Power Platform* by Vikash Kumar; the book itself is not part of this repository, and its text and figures are © Vikash Kumar, all rights reserved.
