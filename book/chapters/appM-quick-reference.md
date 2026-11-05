---
chapter: "M"
appendix: true
title: "Command and Syntax Quick Reference"
---

The commands, expressions and codes used most often in this book, collected for quick lookup. Angle brackets mark values you replace. Check the tools' own help (`--help`) for options not listed here; command options change between versions.

## Azure Functions Core Tools

::: {#tbl:qrfunc caption="Core Tools commands"}
| Task | Command |
|---|---|
| Start the host in the current project | `func start` |
| Start with verbose host logging | `func start --verbose` |
| Publish to an existing Function App | `func azure functionapp publish <app-name>` |
| Fetch a Function App's settings into `local.settings.json` | `func azure functionapp fetch-app-settings <app-name>` |
| Show the Core Tools version | `func --version` |
:::

Run a non-HTTP function on demand (locally no key is needed; in Azure send the master key):

```bash
curl -X POST http://localhost:7071/admin/functions/<FunctionName> \
     -H "Content-Type: application/json" -d '{ "input": "" }'
```

## .NET

::: {#tbl:qrdotnet caption=".NET commands used with the companion solution"}
| Task | Command |
|---|---|
| Build the solution | `dotnet build HarbourlineConnect.slnx` |
| Run all tests (Microsoft Testing Platform) | `dotnet test --solution HarbourlineConnect.slnx` |
| Run one test class (xUnit v3) | `dotnet test --project <test-project> --filter-class "*<ClassName>"` |
| Publish the Functions project | `dotnet publish src/Harbourline.Connect.Functions -c Release -o publish` |
| List outdated packages | `dotnet list package --outdated` |
| Run a sample tool | `dotnet run --project samples/<Project> -- <arguments>` |
:::

## Azure CLI

::: {#tbl:qraz caption="Azure CLI commands for Function Apps"}
| Task | Command |
|---|---|
| Sign in / show the current account | `az login` / `az account show` |
| Get a token for Dataverse (labs only) | `az account get-access-token --resource https://<org>.crm11.dynamics.com` |
| Create a Flex Consumption app | `az functionapp create -g <rg> -n <app> --storage-account <st> --flexconsumption-location <region> --runtime dotnet-isolated --runtime-version 10.0` |
| Show scale settings | `az functionapp scale config show -g <rg> -n <app>` |
| Change instance memory | `az functionapp scale config set -g <rg> -n <app> --instance-memory 2048` |
| Set always-ready instances | `az functionapp scale config always-ready set -g <rg> -n <app> --settings http=2` |
| List, set and delete app settings | `az functionapp config appsettings list / set / delete -g <rg> -n <app> …` |
| List a function's keys | `az functionapp function keys list -g <rg> -n <app> --function-name <fn>` |
| List host and system keys | `az functionapp keys list -g <rg> -n <app>` |
| Show the managed identity | `az functionapp identity show -g <rg> -n <app>` |
| Preview a Bicep deployment | `az deployment group what-if -g <rg> --template-file infra/main.bicep --parameters …` |
| Deploy a Bicep template | `az deployment group create -g <rg> --template-file infra/main.bicep --parameters …` |
| Upload a blob with your identity | `az storage blob upload --auth-mode login --account-name <st> -c <container> -n <name> -f <file>` |
:::

## NCRONTAB expressions

Six fields, seconds first: `{second} {minute} {hour} {day} {month} {day-of-week}`. Schedules are evaluated in UTC unless the app is configured otherwise.

::: {#tbl:qrcron caption="NCRONTAB patterns"}
| Expression | Meaning |
|---|---|
| `0 */5 * * * *` | Every five minutes |
| `30 */5 * * * *` | Every five minutes, at 30 seconds past |
| `0 0 * * * *` | Every hour, on the hour |
| `0 0 2 * * *` | Daily at 02:00 |
| `0 30 6 * * 1-5` | 06:30 on weekdays |
| `0 0 1 1 * *` | 01:00 on the first of each month |
| `0 0 8-17 * * 1-5` | Hourly from 08:00 to 17:00 on weekdays |
:::

`*/n` means "every n", `a-b` a range, `a,b` a list. Day of week: 0 = Sunday.

## Dataverse error codes handled in the companion code

::: {#tbl:qrcodes caption="Dataverse error codes and how the code treats them"}
| Code | Name | Treated as |
|---|---|---|
| `0x80040217` | ObjectDoesNotExist | Not found (404) |
| `0x80060891` | RecordNotFoundByEntityKey | Not found (404) |
| `0x80040237` | DuplicateRecord | Conflict (409) |
| `0x80060892` | DuplicateRecordEntityKey | Conflict (409) |
| `0x80060882` | ConcurrencyVersionMismatch | Precondition failed (412) |
| `0x80040220` | PrivilegeDenied | Permanent; not mapped to a client error |
| `0x80040203` | InvalidArgument | Permanent |
| `0x80072322` | Throttling: number of requests | Transient; honour `Retry-After` |
| `0x80072321` | Throttling: execution time | Transient; honour `Retry-After` |
| `0x80072326` | Throttling: concurrent requests | Transient; honour `Retry-After` |
:::

The classification lives in `src/Harbourline.Connect.Core/Dataverse/DataverseErrors.cs`.

## Web API query options

::: {#tbl:qrodata caption="OData query options and headers"}
| Option or header | Example | Purpose |
|---|---|---|
| `$select` | `$select=hl_name,hl_bedrooms` | Return only these columns |
| `$filter` | `$filter=hl_city eq 'Leeds' and hl_bedrooms ge 2` | Filter rows |
| `$orderby` | `$orderby=hl_name asc` | Sort |
| `$top` | `$top=10` | First n rows only (no paging) |
| `$expand` | `$expand=hl_propertyid($select=hl_name)` | Include related rows |
| Alternate key | `hl_properties(hl_propertycode='HBR-0042')` | Address a row by business key |
| `fetchXml` | `?fetchXml=<url-encoded FetchXML>` | Run FetchXML through the Web API |
| `Prefer: odata.maxpagesize=n` | `odata.maxpagesize=500` | Page size; follow `@odata.nextLink` |
| `Prefer: odata.include-annotations=…` | `"OData.Community.Display.V1.FormattedValue"` | Formatted values for choices, money and dates |
| `If-Match` | `If-Match: W/"1874402"` | Update only if the row version matches |
:::

## Service Bus settlement

::: {#tbl:qrsb caption="Settlement actions in the isolated worker"}
| Action (`ServiceBusMessageActions`) | Effect | Use when |
|---|---|---|
| `CompleteMessageAsync` | Removes the message | Processing succeeded |
| `AbandonMessageAsync` | Releases the lock; delivery count + 1 | Transient failure; retry soon |
| `DeadLetterMessageAsync` | Moves the message to the dead-letter queue with a reason | Permanent failure, or too many attempts |
| `DeferMessageAsync` | Sets the message aside to receive later by sequence number | Waiting for another message first (rarely needed) |
:::

Set `AutoCompleteMessages = false` on the trigger when you settle explicitly.

## KQL patterns

```kusto
// Failures per function, last 24 hours
requests
| where timestamp > ago(24h)
| summarize total = count(), failed = countif(success == false) by name

// Everything in one operation, in order
union requests, dependencies, traces, exceptions
| where operation_Id == "<operation-id>"
| order by timestamp asc

// Slowest dependencies by target, last hour
dependencies
| where timestamp > ago(1h)
| summarize p95 = percentile(duration, 95), calls = count() by target
| order by p95 desc

// Log lines with a business key
traces
| where customDimensions.OrderNumber == "<order-number>"
| project timestamp, severityLevel, message
```

Application Insights' classic table names (`requests`, `traces`, …) are used in its Logs view; in the Log Analytics workspace the same data is in `AppRequests`, `AppTraces` and so on, with slightly different column names.
