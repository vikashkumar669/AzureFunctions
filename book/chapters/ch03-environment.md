---
chapter: 3
part: "Part I — Foundations"
title: "Development Environment Setup"
build-summary: "Your laptop runs Harbourline Connect end to end: the Functions host, local storage, mock partner APIs and a real developer Dataverse environment, all without a secret."
builds:
  - "An Azure subscription layout with naming and tagging rules"
  - "A workstation with the .NET 10 SDK, Core Tools, Azurite and the Azure CLI"
  - "The Harbourline Connect solution, built, tested and running locally"
versions: ".NET 10 SDK · Azure Functions Core Tools 4.x · Azurite · Azure CLI · Power Platform CLI"
---

::: objectives
- Lay out resource groups for three environments and name resources so anyone can tell what they are.
- Install and check the tools this book uses, and know what each one is for.
- Clone, build and test the companion solution, and explain how its projects depend on each other.
- Run the Function App locally against Azurite, the mock APIs and a developer Dataverse environment.
- Debug a function with breakpoints in Visual Studio Code or Visual Studio.
:::

## The business problem

Harbourline's first integration lived on one developer's laptop. When that developer left, nobody could build it, and the only copy of its settings was a screenshot. Priya's team wants the opposite: any developer, on any laptop, should go from a fresh clone to a running app in under an hour, using only their own sign-in.

That needs three things. A predictable layout in Azure, so people find resources without asking. The same tools, at known versions, on every workstation. And a solution that runs locally without shared passwords.

## How it works

Local development with Azure Functions has four moving parts (@fig:devloop).

**Azure Functions Core Tools** (`func`) runs the same Functions host as Azure, on your machine. It reads `host.json` and `local.settings.json`, starts your isolated worker, and listens on port 7071.

**Azurite** emulates Blob, Queue and Table storage. The host needs storage for locks and timers, and `UseDevelopmentStorage=true` points it at Azurite.

The **Azure CLI** signs you in. `DefaultAzureCredential` (Chapter 5) finds that sign-in and gets tokens with it, so the same code that uses a managed identity in Azure uses *you* on your laptop.

A **developer environment** in Power Platform gives you your own Dataverse. You are its administrator, so you can create tables and break things without affecting anyone.

![On a laptop, Core Tools runs your code, Azurite stands in for storage, and your own sign-in reaches a developer Dataverse environment.](figures/ch03/devloop.svg){#fig:devloop}

## Architecture: environments and naming

Harbourline uses one resource group per environment and per workload. @tbl:groups shows the layout.

::: {#tbl:groups caption="Resource groups for Harbourline Connect"}
| Resource group | Environment | Dataverse environment | Who deploys |
|---|---|---|---|
| `rg-hlconnect-dev` | Development | Harbourline-Dev (developer type) | Developers and the pipeline |
| `rg-hlconnect-test` | Test | Harbourline-Test (sandbox) | Pipeline only |
| `rg-hlconnect-prod` | Production | Harbourline (production) | Pipeline only, after approval |
:::

Names follow the Cloud Adoption Framework pattern *type-workload-environment-suffix*, using Microsoft's recommended abbreviations: `func` for a Function App, `st` for a storage account, `sb` for Service Bus, `kv` for Key Vault, `appi` for Application Insights and `log` for a Log Analytics workspace. The Bicep templates in Chapter 20 add a short unique suffix, because storage account and Key Vault names must be globally unique.

Every resource carries three tags: `application`, `environment` and `owner`. Tags cost nothing, and they answer the two questions finance and operations ask most: whose is this, and is it production?

::: tip
Storage account names allow only lower-case letters and digits, 3 to 24 characters. Design the pattern with the strictest resource in mind, and the rest will fit.
:::

## Prerequisites

- An Azure subscription where you can create resource groups. A Visual Studio subscription's monthly credit is enough for this book.
- A Power Platform developer environment. Sign up through the Power Apps Developer Plan; it is free for individual use.
- Git, and access to the companion repository: `github.com/vikashkumar669/AzureFunctions`.

## Step-by-step implementation

### Step 1 – Install the tools

@tbl:tools lists each tool, why you need it and how to check it. Install them from their Microsoft Learn pages, which give the current command for each operating system.

::: {#tbl:tools caption="Workstation tools"}
| Tool | Needed for | Check with | Expect |
|---|---|---|---|
| .NET 10 SDK | Building every project | `dotnet --version` | `10.0.x` |
| Azure Functions Core Tools | Running functions locally | `func --version` | `4.x` |
| Azurite | Local storage | `azurite --version` | Any current version |
| Azure CLI | Signing in, deploying | `az version` | Any current version |
| Power Platform CLI | Solutions, environments | `pac help` | Any current version |
| VS Code + C# Dev Kit + Azure Functions extension, or Visual Studio 2026 | Editing and debugging | Open the solution | Projects load without errors |
| Docker (optional) | Durable Task Scheduler emulator (Chapter 12) | `docker version` | Engine running |
:::

Core Tools can be installed with the Windows installer, Homebrew, a Linux package or npm (`npm i -g azure-functions-core-tools@4`). Use one method only, and upgrade with the same method you installed with.

::: note
**If `func --version` prints 3.x or 2.x:** an old installation is first on your `PATH`. Only one version of Core Tools can be installed on a computer; remove the old one, then reinstall.
:::

### Step 2 – Create the resource group

Sign in, pick the subscription, and create the development group with its tags.

```bash
az login
az account set --subscription "<your-subscription>"
az group create --name rg-hlconnect-dev --location uksouth \
  --tags application=harbourline-connect environment=dev \
         owner=integration-team
```

Expected result: JSON describing the group, with `"provisioningState": "Succeeded"`. You create the resources inside it in Chapter 20, from Bicep; until then everything runs locally.

### Step 3 – Clone and build the solution

```bash
git clone https://github.com/vikashkumar669/AzureFunctions.git
cd AzureFunctions
dotnet build HarbourlineConnect.slnx
dotnet test --solution HarbourlineConnect.slnx
```

The build restores packages, compiles every project and generates the Functions extension metadata. The test run should report every test passing. If a test fails on a fresh clone, stop and fix it before writing code: something on the machine differs from the CI build that runs the same commands.

@tbl:projects explains the projects. The rule that makes the solution testable is in the last column.

::: {#tbl:projects caption="Projects in HarbourlineConnect.slnx"}
| Project | Contains | May reference |
|---|---|---|
| `src/Harbourline.Connect.Contracts` | Request and response types callers see | Nothing |
| `src/Harbourline.Connect.Core` | Business logic, Dataverse access, partner clients | Contracts |
| `src/Harbourline.Connect.Functions` | Function entry points, `Program.cs`, `host.json` | Core, Contracts |
| `tests/Harbourline.Connect.UnitTests` | xUnit tests with NSubstitute | Core, Contracts |
| `samples/*` | Setup tool, query probe, mock APIs, operator tools, trigger gallery | Core |
:::

Core never references Azure Functions types. That keeps business rules testable without the Functions host, and it means the same code could run in a console tool or a container if Harbourline ever needs it to.

### Step 4 – Look at the build settings shared by every project

Two files at the root apply to all projects. `global.json` pins the SDK and the Functions build SDK, so every machine and the CI build use the same compiler. `Directory.Build.props` turns on nullable reference types, the latest .NET analyzers, and *warnings as errors*.

```{.json #lst:globaljson include="global.json" caption="global.json pins the .NET SDK, the Functions build SDK and the test runner"}
```

Warnings as errors sounds strict, and it is meant to be. A nullable warning ignored today is a `NullReferenceException` at 2 a.m. later. The one exception is NuGet's security audit (`NU1901`–`NU1904`): it stays a warning so a newly published advisory doesn't stop every build, but CI shows it on every run.

### Step 5 – Configure local settings

The Function App reads its settings from `local.settings.json`, which is git-ignored. Copy the sample and edit it.

```bash
cd src/Harbourline.Connect.Functions
cp local.settings.sample.json local.settings.json
```

Set `Dataverse__Url` to your developer environment's URL, which you find in the Power Platform admin center under **Environments**. Leave the rest for now; later chapters fill them in.

::: security
Never put a password, client secret or storage key in `local.settings.json`, even though it is ignored by Git. Files get zipped, emailed and backed up. Every value in this book's sample file is either a URL, a placeholder, or a value that only works against local emulators.
:::

### Step 6 – Start the emulators

In two terminals, start Azurite and the mock partner APIs. The mocks simulate LedgerOne and SignBridge, including random failures, so you can develop without the real partners (Chapter 8).

```bash
azurite --location ~/.azurite --silent
dotnet run --project samples/Harbourline.Mocks
```

### Step 7 – Run and call the app

From `src/Harbourline.Connect.Functions`, start the host.

```bash
func start
```

Core Tools prints each function, its trigger and, for HTTP functions, its URL. Call the health endpoint:

```bash
curl http://localhost:7071/api/health
```

Expected result: HTTP 200 and a JSON body with `"service":"Harbourline Connect"`, the version and the current UTC time.

::: note
Functions whose triggers need Azure resources you haven't created yet, such as Service Bus, report connection errors at start-up. That is expected until Chapter 11; HTTP and timer functions still run. To silence them while you work on other chapters, disable a function with an app setting such as `AzureWebJobs.ProcessOrder.Disabled` set to `true`.
:::

### Step 8 – Debug

In VS Code, open the repository folder, choose **Run and Debug › Attach to .NET Functions**, and press F5; the Azure Functions extension starts `func` and attaches. In Visual Studio, set `Harbourline.Connect.Functions` as the start-up project and press F5. Put a breakpoint in `HealthFunction.Run`, call the endpoint again, and inspect `request`.

## Screens you will use

::: {.capture #fig:core-tools caption="Core Tools lists every function and its trigger when the host starts." app="Terminal" path="func start in src/Harbourline.Connect.Functions" annotations="1 Worker runtime and version; 2 HTTP functions with their URLs; 3 Non-HTTP functions with their trigger type"}
:::

::: {.capture #fig:pp-env caption="The environment URL you put in Dataverse__Url comes from the Power Platform admin center." app="Power Platform admin center" path="Manage › Environments › Harbourline-Dev" annotations="1 Environment type: Developer; 2 Environment URL; 3 Organization ID (used in Chapter 9)"}
:::

**Expected result:** the environment page shows type *Developer* and an URL ending in `.crm.dynamics.com` or a regional variant such as `.crm11.dynamics.com`.

## Complete working code

Everything in this chapter is configuration; there is no new code. The solution file, `global.json`, `Directory.Build.props`, `.editorconfig` and `local.settings.sample.json` are all at the paths shown in @tbl:projects.

## Configuration

::: {#tbl:settings caption="Settings used in this chapter"}
| Setting | Local value | In Azure | Secret? |
|---|---|---|---|
| `AzureWebJobsStorage` | `UseDevelopmentStorage=true` | `AzureWebJobsStorage__accountName` (identity) | No |
| `FUNCTIONS_WORKER_RUNTIME` | `dotnet-isolated` | Set by the platform on Flex | No |
| `Dataverse__Url` | Your developer environment | Per environment | No |
:::

## Sample input and output

```text
$ curl -s http://localhost:7071/api/health
{"service":"Harbourline Connect","version":"1.0.0.0",
 "checkedAtUtc":"2026-10-27T09:12:44.512+00:00"}
```

## Testing

You already ran the unit tests in Step 3. Run them again after every pull; they take seconds. Your first local integration check is the health call in Step 7, and your second is in Chapter 6, when the app reads its first Dataverse row.

## Common errors and troubleshooting

::: {#tbl:errors caption="Errors you may see in this chapter"}
| Symptom | Likely cause | Fix |
|---|---|---|
| `Missing value for AzureWebJobsStorage` | No `local.settings.json` | Copy the sample file (Step 5) |
| Host waits, then fails on storage | Azurite not running | Start Azurite first |
| `The Functions extensions project was not restored` warning | Build restores the extensions project during the build | Harmless; a restore first removes it |
| Port 7071 in use | Another host still running | Stop it, or `func start --port 7072` |
| `DefaultAzureCredential failed to retrieve a token` | Not signed in, or wrong tenant | `az login --tenant <tenant-id>` |
| Build fails only on your machine | Different SDK | Install the version in `global.json` |
:::

## Security considerations

::: security
Use your own sign-in for local development, never a shared service account. Every request to Dataverse is then audited as you, and when you leave the company, access ends with your account.
:::

## Performance and scalability

::: performance
Local runs tell you nothing about production throughput: one machine, no scale-out, a developer environment with small capacity. Use them for correctness only, and measure performance in Azure (Chapter 15).
:::

## Alternatives and design decisions

Visual Studio and VS Code are both fully supported; choose by habit. VS Code is lighter and the same on every operating system; Visual Studio has the richer debugger and profiler on Windows. The book's instructions use the command line wherever possible, so they work with either.

A second choice is where developers' Dataverse lives. Harbourline gives each developer their own developer environment, rather than sharing one sandbox. Shared sandboxes drift: one person's test data breaks another's test. Developer environments are free per user, and the setup tool in Chapter 4 rebuilds the schema in minutes.

## Best practices

- One resource group per workload per environment; tag every resource.
- Pin the SDK in `global.json` and treat warnings as errors.
- Keep `local.settings.json` free of secrets; sign in instead.
- Keep business logic in Core, free of Functions types.
- Run the same build and test commands locally as CI does.
- Give every developer their own Dataverse environment.

## Hands-on exercise

::: exercise
**Goal:** prove your workstation matches CI.

**Steps:** clone the repository, run the build and tests, start Azurite and the mocks, run the host and call `/api/health`. Then break something on purpose: delete `local.settings.json` and read the error.

**Done when:** the health endpoint returns 200, and you can explain each line of the error you caused.

**Stretch:** add a fourth tag, `costCentre`, to your resource group with `az group update`, and write a one-line rule for when it is required.
:::

## Knowledge check

::: knowledge-check
1. What does Azurite replace, and why does the host need it even for HTTP-only functions?
2. Why does the same code work with your sign-in locally and a managed identity in Azure?
3. Which project may reference which, and why must Core not reference Azure Functions?
4. What does `global.json` guarantee, and what could go wrong without it?
5. Why give each developer their own Dataverse environment?
:::

Answers are in Appendix G.

::: summary
- Core Tools runs the real Functions host locally; Azurite provides its storage.
- Your Azure CLI sign-in replaces the managed identity on your laptop, so no secret is needed.
- One resource group per environment, named by pattern and tagged, keeps Azure navigable.
- Business logic lives in Core, which has no Functions dependency and is easy to test.
- `global.json` and warnings-as-errors make every build behave like CI.
:::

## References

- [Develop Azure Functions locally using Core Tools](https://learn.microsoft.com/azure/azure-functions/functions-run-local) – Microsoft Learn, checked 2026-10-27.
- [Code and test Azure Functions locally](https://learn.microsoft.com/azure/azure-functions/functions-develop-local) – Microsoft Learn, checked 2026-10-27.
- [Use the Azurite emulator for local Azure Storage development](https://learn.microsoft.com/azure/storage/common/storage-use-azurite) – Microsoft Learn, checked 2026-10-27.
- [Abbreviation recommendations for Azure resources](https://learn.microsoft.com/azure/cloud-adoption-framework/ready/azure-best-practices/resource-abbreviations) – Microsoft Learn, checked 2026-10-27.
- [Sign up for the Power Apps Developer Plan](https://learn.microsoft.com/power-platform/developer/plan) – Microsoft Learn, checked 2026-10-27.

<!-- Glossary terms introduced in this chapter: Core Tools, Azurite, developer environment, resource group, tag -->
