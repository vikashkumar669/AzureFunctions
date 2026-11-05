---
chapter: "L"
appendix: true
title: "Companion Code Reference"
---

The chapters show the companion code in short, focused listings. This appendix prints, in full, the files that tie the solution together: the host's start-up and configuration, the infrastructure templates, the API Management policy and contract, the pipelines and the monitoring queries. Like every listing in this book, they're included automatically from the compiled and tested repository, so they match the code exactly at the time of printing. The repository (`github.com/vikashkumar669/AzureFunctions`) is always the current version.

## Solution-wide build settings

`global.json` pins the .NET SDK family, the `Azure.Functions.Sdk` version and the test runner. `Directory.Build.props` applies the same compiler and analyzer settings to every project, including warnings as errors.

```{.json include="global.json" caption="SDK, project SDK and test runner pins"}
```

```{.xml include="Directory.Build.props" caption="Build settings shared by every project"}
```

## Host start-up

`Program.cs` is deliberately short: it builds the host, enables ASP.NET Core integration, and delegates registrations to two extension methods. `HostingExtensions` adds Functions-specific services (OpenTelemetry, Azure clients, middleware); `ServiceCollectionExtensions` in the Core project registers everything that doesn't depend on Functions, which is what makes the Core services testable without a host.

```{.csharp include="src/Harbourline.Connect.Functions/Program.cs" caption="The isolated worker's entry point"}
```

```{.csharp include="src/Harbourline.Connect.Functions/HostingExtensions.cs" caption="Telemetry, Azure clients and middleware registration"}
```

```{.csharp include="src/Harbourline.Connect.Core/ServiceCollectionExtensions.cs" caption="Core service registration, independent of the Functions host"}
```

```{.csharp include="src/Harbourline.Connect.Functions/Middleware/CorrelationMiddleware.cs" caption="Middleware that adds invocation context to every log"}
```

## Host configuration

```{.json include="src/Harbourline.Connect.Functions/host.json" caption="host.json: telemetry mode, timeout, logging and extension settings"}
```

```{.json include="src/Harbourline.Connect.Functions/local.settings.sample.json" caption="Sample local settings (placeholders only; copy to local.settings.json)"}
```

## Infrastructure

The main template creates every Azure resource the Function App needs, with identity-based access throughout: storage without shared keys, Service Bus with a send-only rule for Dataverse, Key Vault with Azure RBAC, the Durable Task Scheduler, monitoring, the Flex Consumption plan and app, and the role assignments that connect them.

```{.bicep include="infra/main.bicep" caption="infra/main.bicep: the complete environment"}
```

```{.bicep include="infra/apim.bicep" caption="infra/apim.bicep: API Management for the partner API"}
```

## API Management policy and contract

```{.xml include="infra/apim/partner-api-policy.xml" caption="Inbound, outbound and error policy for the partner API"}
```

```{.yaml include="infra/apim/partner-api.openapi.yaml" caption="OpenAPI description of the partner API"}
```

## Pipelines

```{.yaml include=".github/workflows/azure-functions-book.yml" caption="Continuous integration: build, test, Bicep and package checks"}
```

```{.yaml include=".github/workflows/azure-functions-deploy.yml" caption="Deployment: build once, deploy to dev, test and prod with OIDC"}
```

```{.yaml include="pipelines/azure-pipelines.yml" caption="The Azure DevOps alternative"}
```

## Monitoring queries

```{.kusto include="infra/monitoring/queries.kql" caption="KQL queries used by the operations workbook and the labs"}
```
