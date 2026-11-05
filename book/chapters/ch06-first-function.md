---
chapter: 6
part: "Part II — Connecting to Dynamics 365 / Dataverse"
title: "Your First Azure Function Connected to Dataverse"
build-summary: "By the end of this chapter, a function running in Azure reads Dataverse rows without a single secret in its configuration."
builds:
  - "Validated, strongly typed settings that stop a bad deployment at start-up"
  - "One shared Dataverse client, registered with dependency injection"
  - "The first Harbourline endpoint, GET /api/v1/properties/{code}, deployed to Flex Consumption"
versions: ".NET 10 · Azure Functions 4.x isolated worker · Flex Consumption · Microsoft.PowerPlatform.Dataverse.Client 1.2.27 · Azure.Identity 1.21.0"
---

::: objectives
- Trace a request through `Program.cs`, dependency injection, a function and a service class.
- Bind settings to a typed options class and validate them when the host starts.
- Register one `ServiceClient` for the app and explain why it is a singleton.
- Read a row by alternate key and map Dataverse types to an API contract.
- Run the endpoint locally, deploy it to Flex Consumption and call it in Azure.
:::

## The business problem

Harbourline is launching a tenant portal. Its first need is simple: show the details of a property, given the code printed on every advert, such as `HBR-0042`. Today the portal team exports properties to a spreadsheet each morning, so tenants see data that is up to a day old.

The portal must never hold Dataverse credentials, and Harbourline's rules from Chapter 5 apply: no secrets in code or settings, least privilege, one identity per app.

## How it works

Every request to Harbourline Connect follows the same path. The Functions host receives the HTTP request and passes it to the isolated worker. The worker creates the function's class through **dependency injection**, supplying its constructor parameters from the services registered in `Program.cs`. The function reads the request, calls a service in Core, and turns the result into a response.

Three design decisions shape the code:

- **Settings are typed and validated.** Configuration values are bound to an options class with validation attributes. If a required value is missing, the host refuses to start, so a bad deployment fails in seconds, visibly, instead of at the first request.
- **The Dataverse client is shared.** Creating a `ServiceClient` authenticates and loads metadata; doing it per request would be slow and would exhaust connections. One instance, registered as a **singleton**, serves all invocations; its methods are safe to call concurrently.
- **Functions are thin.** The function knows HTTP; the service knows Dataverse. Neither knows the other's details, which keeps both testable.

## Architecture

![The function never holds a secret: Microsoft Entra ID issues tokens to the Function App's managed identity, and Dataverse trusts that identity as an application user.](figures/ch06/architecture.svg){#fig:arch}

::: {#tbl:components caption="Components in @fig:arch"}
| Component | Role | Authenticates with | Failure path |
|---|---|---|---|
| Tenant portal | Calls the API | Function key (Chapter 14 adds Entra ID) | 401 if the key is missing |
| Function App | Runs Harbourline Connect | System-assigned managed identity | Fails at start-up if settings are invalid |
| Microsoft Entra ID | Issues access tokens | — | `AuthenticationFailedException` |
| Dataverse | Stores properties | Application user and role (Chapter 5) | 401/403 without a user or role; 429 when throttled |
| Application Insights | Collects logs and traces | Microsoft Entra ID (Chapter 18) | — |
:::

Data flows left to right. The dashed box is the authentication boundary; the amber arrow is the throttling path that Chapter 15 handles in depth.

## Prerequisites

- Chapter 3: the solution builds and runs locally.
- Chapter 4: the schema exists in your developer environment, with a few properties added in the maker portal.
- Chapter 5: you understand application users; you create one for the deployed app in Step 7.

## Step-by-step implementation

### Step 1 – Start the host in Program.cs

```{.csharp #lst:program include="src/Harbourline.Connect.Functions/Program.cs" region="program" caption="Program.cs: the whole start-up of Harbourline Connect"}
```

1. `FunctionsApplication.CreateBuilder` creates a host builder with configuration from app settings, logging and the Functions worker.
2. ASP.NET Core integration lets functions use `HttpRequest` and return `IActionResult`, the types ASP.NET Core developers already know.
3. One extension method registers everything Core needs. `Program.cs` stays short and free of business logic.
4. `Run` starts the worker. Keep start-up free of network calls: the host gives it 30 seconds on Flex Consumption.

`AddHarbourlineFunctions` registers the partner clients, messaging and telemetry that later chapters add. For now, Core's registration is what matters.

### Step 2 – Bind and validate settings

```{.csharp #lst:options include="src/Harbourline.Connect.Core/Dataverse/DataverseOptions.cs" caption="DataverseOptions: one setting, validated"}
```

The app setting `Dataverse__Url` becomes the configuration key `Dataverse:Url`; the double underscore is how environment variables express nesting. The `[Required]` and `[Url]` attributes are checked when the host starts, as @lst:registration shows.

### Step 3 – Register one Dataverse client

```{.csharp #lst:registration include="src/Harbourline.Connect.Core/ServiceCollectionExtensions.cs" region="dataverse-registration" caption="Registering validated settings and a single Dataverse client"}
```

1. `ValidateDataAnnotations` checks the attributes, and `ValidateOnStart` does so when the host starts instead of the first time a function asks for the options.
2. One credential for the whole app; Azure.Identity caches its tokens.
3. The factory from Chapter 5 runs once, the first time anything needs the client.
4. Code depends on `IOrganizationServiceAsync2`, the interface, so unit tests substitute it (Chapter 19).

::: performance
`ServiceClient`'s asynchronous methods are safe to call from many invocations at once. Microsoft's guidance on parallel requests shows exactly this pattern: one client, many concurrent `async` calls. Chapter 15 tunes how many.
:::

### Step 4 – Read a property by its code

The portal knows `HBR-0042`, not a GUID. @lst:get retrieves the row by its alternate key.

```{.csharp #lst:get include="src/Harbourline.Connect.Core/Properties/PropertyService.cs" region="property-get" caption="Retrieving a row by alternate key"}
```

1. An `EntityReference` built from a table, a key column and a value identifies the row without its GUID.
2. A missing row is a normal outcome, not an error: the method returns `null`, and only a Dataverse "not found" is caught. Every other exception still propagates.

### Step 5 – Map Dataverse types to the API contract

The portal must never see `OptionSetValue` or `Money`. The mapper converts a row into the public `PropertyDto` (@lst:map).

```{.csharp #lst:map include="src/Harbourline.Connect.Core/Properties/PropertyMapper.cs" region="property-map" caption="Mapping a Dataverse row to the public contract"}
```

1. `GetAttributeValue<T>` returns `default` when a column is empty or wasn't retrieved, instead of throwing.
2. Currency columns are `Money`; the API exposes a plain `decimal`.
3. The choice's numeric value becomes the enum's name: `Available`, `Let` or `Maintenance`. Labels could be translated; names don't change.
4. Date-only columns arrive as `DateTime`; the contract uses `DateOnly`.
5. `RowVersion` is kept for optimistic concurrency, which Chapter 7 uses.

### Step 6 – Write the function

```{.csharp #lst:fnget include="src/Harbourline.Connect.Functions/Http/PropertyFunctions.cs" region="fn-get" caption="GET /api/v1/properties/{code}"}
```

1. The `{code}` route segment binds to the `code` parameter by name.
2. The row version goes out as an `ETag` header, ready for Chapter 7.

Notice what is *not* here: no `ServiceClient`, no `try/catch` for Dataverse faults, no JSON code. The function translates between HTTP and the service, and nothing else.

Run it locally (Chapter 3, Step 7) and call:

```bash
curl -i http://localhost:7071/api/v1/properties/HBR-0042
```

Locally, function keys are not enforced, so the call works without one.

### Step 7 – Deploy to Flex Consumption

Chapter 20 builds the full infrastructure from Bicep. For a first deployment, the Azure CLI is enough:

```bash
az storage account create -g rg-hlconnect-dev -n <storage-name> \
  --sku Standard_LRS --allow-shared-key-access false
az functionapp create -g rg-hlconnect-dev -n <app-name> \
  --storage-account <storage-name> --flexconsumption-location uksouth \
  --runtime dotnet-isolated --runtime-version 10.0 \
  --assign-identity '[system]'
az functionapp config appsettings set -g rg-hlconnect-dev -n <app-name> \
  --settings Dataverse__Url=https://<your-dev-org>.crm.dynamics.com
```

Then create the application user for the app's managed identity, exactly as in Chapter 5, Steps 2 to 4. Publish the code from the Functions project folder:

```bash
func azure functionapp publish <app-name>
```

Get the function key from the portal (**Functions › GetProperty › Function Keys**) and call the endpoint:

```bash
curl -i "https://<app-name>.azurewebsites.net/api/v1/properties/HBR-0042?code=<function-key>"
```

## Screens you will use

::: {.capture #fig:fn-list caption="The deployed app lists every function; GetProperty is an HTTP function." app="Azure portal" path="Function App › Overview › Functions" annotations="1 GetProperty; 2 Trigger: HTTP; 3 Status: Enabled"}
:::

::: {.capture #fig:fn-key caption="Function keys protect the endpoint until Chapter 14 adds Microsoft Entra ID." app="Azure portal" path="Function App › Functions › GetProperty › Function Keys" annotations="1 default key; 2 Show value; 3 Copy"}
:::

**Expected result:** HTTP 200, an `ETag` header and the property as JSON.

## Complete working code

- `src/Harbourline.Connect.Functions/Program.cs` and `HostingExtensions.cs`
- `src/Harbourline.Connect.Core/ServiceCollectionExtensions.cs`
- `src/Harbourline.Connect.Core/Dataverse/` (options, factory, schema, errors)
- `src/Harbourline.Connect.Core/Properties/` (service, mapper)
- `src/Harbourline.Connect.Functions/Http/PropertyFunctions.cs`

## Configuration

::: {#tbl:settings caption="App settings used in this chapter"}
| Setting | Local (`local.settings.json`) | Azure | Secret? |
|---|---|---|---|
| `AzureWebJobsStorage` | `UseDevelopmentStorage=true` | `AzureWebJobsStorage__accountName` | No |
| `Dataverse__Url` | Developer environment | Environment URL | No |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Not set | Set by Bicep (Chapter 20) | No |
:::

## Sample input and output

```text
GET /api/v1/properties/HBR-0042

HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8
ETag: "4826113"

{"code":"HBR-0042","name":"12 Canal Wharf","city":"Leeds",
 "postcode":"LS11 5PS","bedrooms":2,"monthlyRent":1150.00,
 "status":"Available","availableFrom":"2026-12-01"}
```

(Illustrative response with fictional data.)

## Testing

Run the unit tests with `dotnet test --solution HarbourlineConnect.slnx`. `PropertyMapperTests` checks the choice, money and version mapping; `PropertyServiceTests.Get_ReturnsNull_WhenKeyNotFound` checks that a missing key is `null`, not an exception. Then test by hand: a known code returns 200, an unknown one 404, and the deployed app returns 401 without a key.

## Common errors and troubleshooting

::: {#tbl:errors caption="Errors you may see in this chapter"}
| Symptom | Likely cause | Fix |
|---|---|---|
| Host fails at start-up: `DataAnnotation validation failed for 'DataverseOptions'` | `Dataverse__Url` missing or not a URL | Set the app setting |
| 500 with `PrivilegeDenied` in the logs | Application user lacks `prvReadhl_property` | Add the privilege to the role |
| 500 on the first call after deployment, 200 afterwards | First call pays for authentication and metadata | Expected; see Chapter 15 on warm-up |
| 404 for a code that exists | Key not active, or wrong environment | Check the key status and `Dataverse__Url` |
| 401 in Azure | Missing `code` query parameter | Pass the function key |
:::

## Security considerations

::: security
Function keys are shared secrets, suitable only for a first deployment and internal callers. Anyone who has the key can call the API. Chapter 14 replaces keys with Microsoft Entra ID tokens and puts API Management in front.
:::

## Performance and scalability

::: performance
Retrieve only the columns the contract needs: `PropertyService` names them explicitly. With the client registered once, each request is one Dataverse round trip; that is the baseline Chapter 15 measures against.
:::

## Alternatives and design decisions

Harbourline could have called the Web API with `HttpClient` from the function (Chapter 4). The SDK won because the team writes .NET, because `ServiceClient` handles service protection errors, and because typed request classes like `UpsertMultipleRequest` are used heavily later.

A second option is a scoped `ServiceClient`, one per invocation, cloned from the singleton. Harbourline uses the shared client and keeps `Clone()` for the one place it is needed: impersonating a user, in Chapter 22.

## Best practices

- Keep `Program.cs` short; put registrations in extension methods.
- Validate settings at start-up with `ValidateOnStart`.
- Register `ServiceClient` once and depend on `IOrganizationServiceAsync2`.
- Look rows up by alternate key; never expose GUIDs or Dataverse types to callers.
- Return `null` for "not found" and let real failures throw.

## Hands-on exercise

::: exercise
**Goal:** add a field end to end.

**Steps:** expose the `hl_furnished` column from the Chapter 4 exercise as a `Furnished` flag: add it to `PropertyDto`, retrieve and map it, and return it from the API.

**Done when:** the API returns `"furnished": true` for a furnished property, and a new unit test checks the mapping.

**Stretch:** make the host fail at start-up when `Dataverse__Url` does not use HTTPS, with a custom validation attribute.
:::

## Knowledge check

::: knowledge-check
1. What does `ValidateOnStart` change, and why does it matter in Azure?
2. Why is `ServiceClient` registered as a singleton?
3. How does the function find a property without knowing its GUID?
4. Why does `GetByCodeAsync` return `null` instead of throwing for a missing row?
5. What protects the deployed endpoint in this chapter, and what replaces it later?
:::

Answers are in Appendix G.

::: summary
- `Program.cs` builds the host; extension methods register settings and services.
- Typed, validated options make a misconfigured app fail at start-up, not at the first request.
- One `ServiceClient`, shared through `IOrganizationServiceAsync2`, serves every invocation.
- Functions translate HTTP; services in Core talk to Dataverse; mappers keep Dataverse types out of contracts.
- A first deployment needs the app, its identity, one setting and an application user, and no secret.
:::

## References

- [Guide for running C# Azure Functions in the isolated worker model](https://learn.microsoft.com/azure/azure-functions/dotnet-isolated-process-guide) – Microsoft Learn, checked 2026-10-27.
- [Create and manage function apps in the Flex Consumption plan](https://learn.microsoft.com/azure/azure-functions/flex-consumption-how-to) – Microsoft Learn, checked 2026-10-27.
- [Options pattern in .NET](https://learn.microsoft.com/dotnet/core/extensions/options) – Microsoft Learn, checked 2026-10-27.
- [Send parallel requests](https://learn.microsoft.com/power-apps/developer/data-platform/send-parallel-requests) – Microsoft Learn, checked 2026-10-27.
- [Use an alternate key to reference a record](https://learn.microsoft.com/power-apps/developer/data-platform/use-alternate-key-reference-record) – Microsoft Learn, checked 2026-10-27.

<!-- Glossary terms introduced in this chapter: dependency injection, singleton, options pattern, function key -->
