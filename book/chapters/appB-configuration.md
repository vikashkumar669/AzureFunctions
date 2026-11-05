---
chapter: "B"
appendix: true
title: "Configuration Reference"
---

Every setting Harbourline Connect reads, where it is set and whether it is secret. In app settings, a double underscore (`__`) separates sections: `Dataverse__Url` is the configuration key `Dataverse:Url`.

## App settings

::: {#tbl:appsettings caption="App settings of Harbourline Connect"}
| Setting | Used by | Example / value | Secret? | Chapter |
|---|---|---|---|---|
| `AzureWebJobsStorage__accountName` | Host storage (identity) | `sthlconnect…` | No | 20 |
| `AzureWebJobsStorage` | Host storage (local) | `UseDevelopmentStorage=true` | No | 3 |
| `FUNCTIONS_WORKER_RUNTIME` | Host (local) | `dotnet-isolated` | No | 3 |
| `Dataverse__Url` | `DataverseOptions` | `https://<org>.crm11.dynamics.com` | No | 6 |
| `Dataverse__OrganizationId` | Webhook check | GUID of the environment | No | 9 |
| `LedgerOne__BaseUrl` | LedgerOne client | Partner URL | No | 8 |
| `LedgerOne__ApiKey` | LedgerOne client | Key Vault reference | Yes | 8 |
| `SignBridge__BaseUrl` | SignBridge client | Partner URL | No | 8 |
| `SignBridge__Scope` | Token handler | `api://<app-id>/.default` | No | 8 |
| `SignBridge__CallbackSecret` | Callback HMAC | Key Vault reference | Yes | 12 |
| `ServiceBus__fullyQualifiedNamespace` | Triggers, sender | `<ns>.servicebus.windows.net` | No | 11 |
| `FeedStorage__blobServiceUri` | Blob triggers (identity) | Blob endpoint | No | 13 |
| `FeedStorage__queueServiceUri` | Blob triggers (identity) | Queue endpoint | No | 13 |
| `Exports__BlobServiceUri` | Lease packs | Blob endpoint | No | 22 |
| `DURABLE_TASK_SCHEDULER_CONNECTION_STRING` | Durable Functions | `Endpoint=…;TaskHub=default;Authentication=ManagedIdentity` | No | 12 |
| `InvoiceSyncSchedule` | Invoice timer | `0 0 2 * * *` | No | 10 |
| `Bulk__BatchSize`, `Bulk__Parallelism` | Bulk loader | `200`, server hint | No | 15 |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Telemetry | From Bicep | No | 18 |
| `APPLICATIONINSIGHTS_AUTHENTICATION_STRING` | Telemetry (Entra) | `Authorization=AAD` | No | 18 |
| `AzureWebJobs.<Function>.Disabled` | Disable one function | `true` | No | 3 |
:::

## host.json

::: {#tbl:hostjson caption="host.json settings used"}
| Path | Value | Purpose | Chapter |
|---|---|---|---|
| `version` | `2.0` | Schema version | 2 |
| `telemetryMode` | `OpenTelemetry` | Host telemetry via OTel | 18 |
| `functionTimeout` | `00:30:00` | Explicit default on Flex | 2 |
| `logging.logLevel.default` | `Information` | Production level | 18 |
| `extensions.serviceBus.maxConcurrentCalls` | `8` | Messages per instance | 11 |
| `extensions.serviceBus.maxAutoLockRenewalDuration` | `00:10:00` | Lock renewal for slow work | 11 |
| `extensions.durableTask.storageProvider.type` | `azureManaged` | Durable Task Scheduler | 12 |
:::

## Flex Consumption app configuration (Bicep `functionAppConfig`)

::: {#tbl:flexconfig caption="functionAppConfig values"}
| Property | Value | Chapter |
|---|---|---|
| `deployment.storage` | Blob container `deployments`, system-assigned identity | 20 |
| `runtime` | `dotnet-isolated`, `10.0` | 20 |
| `scaleAndConcurrency.instanceMemoryMB` | `2048` | 2 |
| `scaleAndConcurrency.maximumInstanceCount` | Parameter, default 40 | 2, 15 |
| `siteUpdateStrategy.type` | `RollingUpdate` | 20 |
:::

## Roles granted to the app's identity

::: {#tbl:roles caption="Azure role assignments"}
| Scope | Role | Why |
|---|---|---|
| Storage account | Storage Blob Data Owner | Host, deployment, feeds, packs, user delegation keys |
| Storage account | Storage Queue Data Contributor | Host and blob-trigger queues |
| Service Bus namespace | Azure Service Bus Data Receiver, Data Sender | Triggers and the order sender |
| Key Vault | Key Vault Secrets User | Key Vault references |
| Application Insights | Monitoring Metrics Publisher | Entra-authenticated telemetry |
| Durable Task Scheduler task hub | Durable Task Data Contributor | Orchestration state |
:::
