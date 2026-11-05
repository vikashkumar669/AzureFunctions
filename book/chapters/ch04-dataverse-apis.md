---
chapter: 4
part: "Part II — Connecting to Dynamics 365 / Dataverse"
title: "Understanding the Dataverse Web API and SDK"
build-summary: "You create Harbourline's tables from code and answer one business question three ways, so you can choose the right Dataverse API for every function that follows."
builds:
  - "A setup tool that creates the Harbourline solution, tables, keys and lookups"
  - "A probe that runs the same query with QueryExpression, FetchXML and the Web API"
  - "A decision guide for SDK versus Web API in Azure Functions"
versions: ".NET 10 · Microsoft.PowerPlatform.Dataverse.Client 1.2.27 · Dataverse Web API v9.2"
---

::: objectives
- Describe the two ways code reaches Dataverse and what they share.
- Create tables, columns, choices, alternate keys and lookups with the metadata API.
- Write the same query as a QueryExpression, as FetchXML and as an OData request.
- Read choices, lookups, money and formatted values correctly in each API.
- Page through large results in both APIs without losing or repeating rows.
- Choose between the SDK and the Web API for a given function.
:::

## The business problem

Harbourline's data lives in a handful of custom tables: properties, leases, invoices, service orders, meter readings. Every integration in this book reads or writes them. Before writing integrations, the team needs two things: the same tables in every developer's environment, and a shared understanding of how code talks to them.

The second matters more than it sounds. Two developers on the old team wrote the same lookup in two styles, one with the SDK and one with raw HTTP calls, and they handled paging differently. One of them silently stopped at 5,000 rows.

## How it works

Dataverse exposes its services through two programming interfaces (@fig:apis).

The **SDK for .NET** is a set of assemblies. Its main entry point in modern .NET is `ServiceClient` from the `Microsoft.PowerPlatform.Dataverse.Client` package, which implements `IOrganizationService` and its asynchronous sibling `IOrganizationServiceAsync2`. You work with typed objects: `Entity`, `EntityReference`, `OptionSetValue`, `Money`, and requests such as `UpsertRequest`.

The **Web API** is a RESTful endpoint that implements OData version 4.0, at `https://<org>/api/data/v9.2/`. Any language that can send HTTP and read JSON can use it.

Both reach the same tables, enforce the same security roles, run the same plug-ins and count against the same service protection limits. The choice between them is about your code, not about Dataverse.

![The SDK for .NET and the Web API are two doors into the same Dataverse services.](figures/ch04/apis.svg){#fig:apis}

### Tables, columns and keys

A **table** (in the SDK, an *entity*) has a **logical name**, such as `hl_property`, and each **column** (an *attribute*) has one too, such as `hl_monthlyrent`. The prefix `hl_` comes from Harbourline's **publisher**; every customization in a solution carries its publisher's prefix.

Every row has a GUID primary key. An **alternate key** is a column, or set of columns, whose values are unique and which you can use instead of the GUID to identify a row. Harbourline's integrations rely on alternate keys everywhere: partner systems know a property as `HBR-0042`, never as a GUID, and alternate keys make **upsert** possible: "update the row with this key, or create it".

### Column types you will meet

@tbl:types shows how the main column types look in each API. The Web API returns raw values unless you ask for annotations.

::: {#tbl:types caption="How common column types appear in each API"}
| Column type | SDK for .NET | Web API (JSON) |
|---|---|---|
| Text | `string` | string |
| Whole number | `int` | number |
| Currency | `Money` (use `.Value`) | number, plus `_transactioncurrencyid_value` |
| Choice | `OptionSetValue` (use `.Value`) | number |
| Date only | `DateTime` | `"2026-11-01"` |
| Lookup | `EntityReference` (Id, LogicalName, Name) | `_hl_propertyid_value` (GUID) |
| Formatted value | `entity.FormattedValues["hl_status"]` | `hl_status@OData.Community.Display.V1.FormattedValue` |
:::

## Architecture

The setup tool (`samples/Harbourline.Setup`) and the probe (`samples/Harbourline.Probe`) are console apps. Both sign in with `DefaultAzureCredential` and reuse `DataverseClientFactory` from Core (Chapter 6 explains it). @tbl:components lists what each touches.

::: {#tbl:components caption="Components in this chapter"}
| Component | Role | Authenticates with | Failure path |
|---|---|---|---|
| Setup tool | Creates solution and schema | Your sign-in (System Customizer or System Administrator) | Fault naming the missing privilege |
| Probe | Runs one query three ways | Your sign-in (any role that can read `hl_property`) | Empty results or 403 |
| Dataverse | Stores tables and metadata | Microsoft Entra ID tokens | — |
:::

## Prerequisites

- Chapter 3: tools installed, signed in with `az login`, developer environment URL at hand.
- You are an administrator in your **developer** environment. Never run the setup tool against a shared or production environment; ship schema there in a solution (Chapter 20).

## Step-by-step implementation

### Step 1 – Create the publisher and solution

Customizations belong in a solution, owned by a publisher. @lst:solution creates both, unless they exist.

```{.csharp #lst:solution include="samples/Harbourline.Setup/Program.cs" region="setup-solution" caption="Creating Harbourline's publisher and unmanaged solution"}
```

1. The **customization prefix** goes in front of every name: `hl_property`.
2. The **option value prefix** sets choice values: prefix 10,000 makes them start at 100,000,000. The `PropertyStatus` enum in Core uses exactly these values.
3. Creating the solution row is enough; components added with its unique name land inside it.

### Step 2 – Create the tables

`EnsureTableAsync` (@lst:table) checks first, then sends a `CreateEntityRequest`. A table always has a primary name column, created with it.

```{.csharp #lst:table include="samples/Harbourline.Setup/SchemaBuilder.cs" region="setup-table" caption="Creating a table only if it doesn't exist"}
```

1. Running the tool twice must be safe. Every `Ensure` method checks before it creates.
2. User-owned tables support row-level security by owner and business unit.
3. Change tracking is needed for `RetrieveEntityChanges` (Chapter 10) and for Azure Synapse Link. Enable it only where you need it: once on, it cannot be turned off.
4. `SolutionUniqueName` adds the new table to Harbourline's solution.

### Step 3 – Add columns

@lst:columns adds the property columns. Each helper builds a metadata object with a schema name, display name and type-specific settings, such as the range of a whole number or the behaviour of a date.

```{.csharp #lst:columns include="samples/Harbourline.Setup/Program.cs" region="setup-columns" caption="Property columns: text, number, currency, choice and date only"}
```

Two settings deserve a word. `DateOnly` behaviour stores a date with no time zone: a lease that starts on 1 November starts on 1 November everywhere. And the choice values come from the publisher's prefix, so they match the constants in `Schema.cs` (@lst:schema).

```{.csharp #lst:schema include="src/Harbourline.Connect.Core/Dataverse/Schema.cs" region="schema-property" caption="Logical names and choice values as constants in Core"}
```

Integration code refers to these constants, never to string literals. A typo in a string compiles; a typo in a constant name doesn't.

### Step 4 – Add alternate keys and lookups

```{.csharp #lst:key include="samples/Harbourline.Setup/SchemaBuilder.cs" region="setup-key" caption="Adding an alternate key"}
```

1. A key can span several columns; Harbourline's keys use one.
2. Dataverse builds an index for the key in the background. Until the key's status is *Active*, upserts by that key fail. Check it in the maker portal under the table's **Keys**.

The tool also creates lookups with `CreateOneToManyRequest`: lease to property, lease to tenant (contact), invoice to lease, order to property, meter reading to property. Delete behaviour is *Restrict*, so a property with leases can't be deleted by accident.

Run the tool:

```bash
dotnet run --project samples/Harbourline.Setup -- \
  --url https://<your-dev-org>.crm.dynamics.com
```

Expected result: one line per created component, then `Done. Publish customizations in the maker portal.` Run it again and every line says it exists.

### Step 5 – Query with QueryExpression

The probe asks one question: *available properties in Leeds with two or more bedrooms, by name*. @lst:qe asks it with the SDK's object model.

```{.csharp #lst:qe include="samples/Harbourline.Probe/Program.cs" region="probe-qe" caption="The question as a QueryExpression"}
```

1. Always name the columns you need. `new ColumnSet(true)` retrieves every column, which is slower and fragile when columns are added.
2. Choices are compared by value, not by label. Labels can be translated and renamed; values can't.

### Step 6 – Query with FetchXML

FetchXML is Dataverse's own query language: XML that you can build in Advanced Find or the FetchXML Builder in the XrmToolBox, paste into code, and run through the same `RetrieveMultipleAsync`.

```{.csharp #lst:fetch include="samples/Harbourline.Probe/Program.cs" region="probe-fetch" caption="The same question as FetchXML"}
```

1. A C# raw string literal keeps the XML readable. Build values into FetchXML with care: escape anything that comes from a user.
2. `FetchExpression` wraps the XML; the call and the result type are the same as for a QueryExpression.

### Step 7 – Query with the Web API

```{.csharp #lst:webapi include="samples/Harbourline.Probe/Program.cs" region="probe-webapi" caption="The same question as an OData request"}
```

1. Version 9.2 is the current Web API version. The entity set name, `hl_properties`, is the table's plural logical collection name.
2. Ask for annotations to get formatted values (labels of choices, names of lookups) alongside raw values.
3. The OData query options map directly to the QueryExpression: `$select`, `$filter`, `$orderby` and `$top`.

Run the probe after creating a few properties in the maker portal:

```bash
dotnet run --project samples/Harbourline.Probe -- \
  https://<your-dev-org>.crm.dynamics.com
```

### Step 8 – Page through large results

Both APIs return at most 5,000 rows per request. Code that ignores paging works in development and silently loses data in production, the bug Harbourline's old team shipped.

- **SDK:** set `PageInfo.Count` and `PageNumber`, and after each page copy `PagingCookie` from the result into the query, until `MoreRecords` is false. Chapter 7's `PropertyService.ListAsync` and Chapter 16's reconciler both do this.
- **Web API:** send `Prefer: odata.maxpagesize=<n>`, then follow the `@odata.nextLink` URL in each response until there isn't one.

::: warning
Don't page with `$skip` or by incrementing a page number without the paging cookie. Rows inserted or deleted between requests then shift the pages, and you miss or repeat rows. Always pass the cookie or follow `@odata.nextLink`.
:::

## Screens you will use

::: {.capture #fig:keys caption="An alternate key must be Active before upserts by that key succeed." app="Power Apps maker portal" path="Solutions › Harbourline Connect › Property › Keys" annotations="1 Key name hl_propertycode_key; 2 Key column; 3 Status: Active"}
:::

::: {.capture #fig:probe caption="The probe returns the same rows from all three APIs." app="Terminal" path="dotnet run --project samples/Harbourline.Probe" annotations="1 QueryExpression results; 2 FetchXML results; 3 Web API results"}
:::

**Expected result:** three blocks with the same count and the same names in the same order.

## Complete working code

- `samples/Harbourline.Setup/Program.cs` and `SchemaBuilder.cs`: the full schema, including leases, invoices, service orders, meter readings, sync state and two columns added to `account`.
- `samples/Harbourline.Probe/Program.cs`: the three queries.
- `src/Harbourline.Connect.Core/Dataverse/Schema.cs`: all logical names and choice values.

## Configuration

Both console apps take the environment URL as an argument or from the `Dataverse__Url` environment variable. Neither needs any other setting, and neither stores a credential.

## Sample input and output

```text
QueryExpression: 2 rows
  12 Canal Wharf (2 bed)
  4 Park Row (3 bed)
FetchXML: 2 rows
  12 Canal Wharf (2 bed)
  4 Park Row (3 bed)
Web API (OData): 2 rows
  12 Canal Wharf (2 bed)
  4 Park Row (3 bed)
```

(Illustrative output with fictional data.)

## Testing

Setup code is tested by running it twice: the second run must change nothing. For queries, the unit tests in later chapters substitute `IOrganizationServiceAsync2` and assert on the `QueryExpression` the code builds (Chapter 7 shows how), which catches a wrong filter without a Dataverse call.

## Common errors and troubleshooting

::: {#tbl:errors caption="Errors you may see in this chapter"}
| Symptom | Likely cause | Fix |
|---|---|---|
| `PrivilegeDenied` (0x80040220) when creating a table | Your role lacks customization privileges | Use System Customizer or Administrator in your developer environment |
| `Could not find entity hl_property` in a query | Wrong logical name or schema not created | Check the name in `Schema.cs`; run the setup tool |
| Web API 404 on `hl_property` | Used the logical name, not the entity set name | Use `hl_properties` |
| Upsert by key fails right after creating the key | Key index still building | Wait until the key status is Active |
| Choice filter returns nothing | Filtered on the label | Filter on the value (`100000000`) |
| Exactly 5,000 rows every time | Paging not implemented | Follow the paging cookie or `@odata.nextLink` |
:::

## Security considerations

::: security
The setup tool needs customization privileges; the integration never does. Harbourline's application user (Chapter 5) gets a role that reads and writes rows in the `hl_` tables and nothing else. Schema changes reach test and production only as solutions, through the pipeline.
:::

## Performance and scalability

::: performance
Select only the columns you need, filter on the server, and ask for formatted values only when you display them. Microsoft's query guidance also warns against leading wildcards in `like` conditions and against sorting on columns of related tables, both of which can make queries much slower on large tables.
:::

## Alternatives and design decisions

@tbl:choose summarises when Harbourline uses each API.

::: {#tbl:choose caption="SDK or Web API?"}
| Situation | Use | Why |
|---|---|---|
| .NET code in Azure Functions (most of this book) | SDK (`ServiceClient`) | Typed objects, request classes, built-in handling of service protection errors |
| Non-.NET caller (Python, JavaScript) | Web API | Language-neutral HTTP and JSON |
| Calls from Power Automate or a custom connector | Web API | Standard OpenAPI-friendly HTTP |
| Smallest possible dependency footprint | Web API with `HttpClient` | No SDK package |
| Bulk messages, metadata, Custom APIs from .NET | SDK | Request classes exist for every message |
:::

Between QueryExpression and FetchXML the choice is mostly taste. QueryExpression is checked by the compiler and easy to build conditionally, which is why Harbourline uses it in code. FetchXML is easy to copy from tools and supports a few features first, such as some aggregation options; use it when you need those.

## Best practices

- Keep logical names and choice values as constants in one file.
- Select explicit columns; never retrieve all columns in integration code.
- Compare choices by value and display them by formatted value.
- Give every integration-facing table an alternate key.
- Implement paging in every query that can return more than one page.
- Make setup and migration tools idempotent.

## Hands-on exercise

::: exercise
**Goal:** extend the schema and query it three ways.

**Steps:** add an `hl_furnished` Yes/No column to `hl_property` in the setup tool (`BooleanAttributeMetadata`). Run the tool twice. Then extend the probe so each query also filters on furnished properties.

**Done when:** the second run of the setup tool creates nothing, and all three probe queries return the same rows.

**Stretch:** make the Web API version of the probe follow `@odata.nextLink` with `Prefer: odata.maxpagesize=2` and print each page.
:::

## Knowledge check

::: knowledge-check
1. Name three things the SDK and the Web API have in common.
2. Why do Harbourline's choice values start at 100,000,000?
3. What is an alternate key, and which operation does it make possible?
4. In the Web API, how do you get the label of a choice column?
5. Why is paging with a page number alone unreliable, and what should you use?
:::

Answers are in Appendix G.

::: summary
- The SDK for .NET and the Web API reach the same Dataverse, with the same security, logic and limits.
- Harbourline's schema is created by an idempotent setup tool and described by constants in Core.
- Alternate keys let partners and integrations identify rows by business keys and make upsert possible.
- QueryExpression, FetchXML and OData express the same queries; choose by context.
- Every query that can exceed one page must follow the paging cookie or `@odata.nextLink`.
:::

## References

- [Use the Microsoft Dataverse Web API](https://learn.microsoft.com/power-apps/developer/data-platform/webapi/overview) – Microsoft Learn, checked 2026-10-27.
- [Use the SDK for .NET](https://learn.microsoft.com/power-apps/developer/data-platform/org-service/overview) – Microsoft Learn, checked 2026-10-27.
- [Page results using OData](https://learn.microsoft.com/power-apps/developer/data-platform/webapi/query/page-results) – Microsoft Learn, checked 2026-10-27.
- [Select columns using OData](https://learn.microsoft.com/power-apps/developer/data-platform/webapi/query/select-columns) – Microsoft Learn, checked 2026-10-27.
- [Define alternate keys to reference rows](https://learn.microsoft.com/power-apps/developer/data-platform/define-alternate-keys-entity) – Microsoft Learn, checked 2026-10-27.
- [Query anti-patterns](https://learn.microsoft.com/power-apps/developer/data-platform/query-antipatterns) – Microsoft Learn, checked 2026-10-27.
- [Use change tracking to synchronize data with external systems](https://learn.microsoft.com/power-apps/developer/data-platform/use-change-tracking-synchronize-data-external-systems) – Microsoft Learn, checked 2026-10-27.

<!-- Glossary terms introduced in this chapter: SDK for .NET, Web API, OData, logical name, publisher, alternate key, upsert, FetchXML, QueryExpression, paging cookie, formatted value -->
