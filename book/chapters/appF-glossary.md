---
chapter: "F"
appendix: true
title: "Glossary"
---

Terms are defined as this book uses them; the number in brackets is the chapter that introduces the term.

**Access token** – A signed, time-limited statement from Microsoft Entra ID that an identity may call a resource. (5)

**Activity function** – A Durable Functions function that does one piece of work, such as an API call; the only place for I/O in a workflow. (12)

**Alternate key** – One or more columns with unique values that identify a Dataverse row instead of its GUID; enables upsert. (4)

**Always-ready instance** – A Flex Consumption instance kept running to avoid cold starts, billed while ready. (2)

**API Management (APIM)** – Azure's API gateway: applies policies to requests and forwards them to backends. (14)

**App role** – A role defined on an Entra ID app registration and granted to apps or users; appears in the token's `roles` claim. (14)

**Application user** – A Dataverse user record bound to an Entra ID application or managed identity, with security roles and no licence. (5)

**At-least-once delivery** – A guarantee that a message is processed one or more times; handlers must be idempotent. (11)

**Attempt timeout** – The maximum time one HTTP attempt may take before it is cancelled and possibly retried. (8)

**Azurite** – A local emulator of Azure Blob, Queue and Table storage. (3)

**Bicep** – Azure's declarative language for infrastructure as code. (20)

**Binding** – A declarative connection from a function to another service: input bindings bring data in, output bindings send it out. (2)

**Blob trigger** – Runs a function when a blob is added or changed; on Flex Consumption it uses the Event Grid source only. (13)

**Bulk operation message** – `CreateMultiple`, `UpdateMultiple`, `UpsertMultiple` (and `DeleteMultiple` for elastic tables): many rows of one table per request. (15)

**Change tracking** – A Dataverse table option that lets `RetrieveEntityChanges` return rows changed since a data token. (10)

**Checkpoint** – A saved position (watermark, token or batch number) from which a job resumes. (10)

**Checksum** – A hash over a whole data set; equal checksums mean equal sets. (16)

**Circuit breaker** – A strategy that stops calling a failing system for a while, failing fast instead. (8)

**Client credentials flow** – OAuth 2.0 flow in which an application gets a token as itself, with no user. (14)

**Client principal** – The signed-in caller as passed by App Service authentication in the `X-MS-CLIENT-PRINCIPAL` header. (22)

**Cold start** – The delay while a new instance starts before it can run a function. (2)

**Concurrency** – How many invocations one instance runs at the same time. (2)

**Continuation token** – An opaque string the client sends back to get the next page of results. (7)

**Contract-first** – Designing an API's OpenAPI definition before implementing it, and reviewing it as code. (14)

**Core Tools** – The `func` command-line tool that runs the Functions host locally and publishes apps. (3)

**Custom API** – A Dataverse message you define, callable like built-in messages and implemented by a plug-in. (9)

**Custom connector** – A Power Platform connector built from an OpenAPI definition, so makers can call your API. (9)

**Data token** – The value returned by change tracking that marks the point to read changes from next time. (10)

**Dead-letter queue (DLQ)** – A Service Bus sub-queue holding messages that couldn't be processed. (11)

**DefaultAzureCredential** – An Azure.Identity credential that tries a chain of sources, from managed identity to developer sign-ins. (5)

**Delivery count** – How many times Service Bus has delivered a message; at the max delivery count it is dead-lettered. (11)

**Dependency injection** – Supplying a class's dependencies through its constructor from a container configured at start-up. (6)

**Deterministic** – Producing the same decisions on every replay; required of orchestrator code. (12)

**Developer environment** – A free, personal Power Platform environment with its own Dataverse. (3)

**Duplicate detection** – Service Bus discards a message whose `MessageId` was seen within a configured window. (11)

**Durable Functions** – An Azure Functions extension for stateful workflows written as code. (12)

**Durable Task Scheduler** – Microsoft's managed backend for Durable Functions state, with a dashboard. (12)

**Durable timer** – A timer created by an orchestration that survives restarts and costs nothing while waiting. (12)

**Easy Auth** – Informal name for App Service authentication: built-in token validation in front of the app. (14)

**Echo** – In two-way sync, a change caused by the sync's own previous write. (21)

**Elastic table** – A Dataverse table backed by Azure Cosmos DB for high-volume data, without transactions. (15)

**Entity image** – A snapshot of a row before or after an operation, attached to a step's execution context. (9)

**ETag** – An HTTP header carrying a resource version; with `If-Match` it enables optimistic concurrency. (7)

**Event Grid** – Azure's event routing service; delivers storage events such as BlobCreated. (13)

**Federated credential** – Trust in Entra ID for tokens from an external issuer (such as GitHub) matching a subject. (20)

**FetchXML** – Dataverse's XML query language. (4)

**Fingerprint** – A canonical string of a row's migrated fields, compared between source and target. (16)

**Formatted value** – The display label of a value, such as a choice's text. (4)

**Function App** – The unit of deployment and configuration in Azure Functions. (2)

**Function key** – A shared secret that authorises calls to HTTP functions at `Function` level. (6)

**Functions host** – The runtime process that listens to triggers and runs bindings. (2)

**HMAC** – A keyed hash used to sign messages; verifies origin and integrity. (12)

**Idempotent** – Having the same effect whether done once or several times. (8)

**Idempotency key** – A value sent with a request so the server can recognise and ignore repeats. (8)

**Impersonation** – Running Dataverse requests as another user, applying that user's security. (22)

**Incremental load** – Processing only what changed since the last run. (10)

**Instance ID** – The identifier of a Durable Functions orchestration instance. (12)

**Isolated worker model** – Running .NET function code in its own process, separate from the host. (2)

**Key Vault reference** – An app setting value that the platform resolves from Azure Key Vault. (5)

**Keyset paging** – Paging by seeking past the last returned row's sort values. (23)

**KQL** – Kusto Query Language, used to query Application Insights and Log Analytics. (18)

**Logical name** – The lower-case system name of a Dataverse table or column, such as `hl_property`. (4)

**Managed identity** – An Entra ID identity that Azure creates and manages for a resource. (5)

**Managed solution** – A packaged, locked set of Dataverse customizations deployed to test and production. (20)

**Mapping specification** – The agreed rules for turning source data into target data. (16)

**NCRONTAB** – The six-field schedule format of timer triggers (seconds first). (2)

**OData** – The REST query protocol implemented by the Dataverse Web API. (4)

**OpenID Connect (OIDC)** – Identity protocol used by pipelines to get Azure tokens without stored secrets. (20)

**OpenTelemetry** – A vendor-neutral standard for traces, metrics and logs. (18)

**Operation ID** – Application Insights' name for the trace ID that joins related telemetry. (18)

**Optimistic concurrency** – Detecting conflicting updates at save time by comparing versions, without locks. (7)

**Orchestrator function** – A Durable Functions function that describes a workflow and is replayed. (12)

**Paging cookie** – Dataverse's marker for where the next page of a query starts. (4)

**Peek-lock** – Service Bus receive mode in which a message is locked, not removed, until settled. (11)

**Permanent failure** – A failure that retrying can't fix, such as invalid data. (24)

**Policy** – An API Management rule applied to requests or responses. (14)

**Problem details** – The RFC 9457 JSON format for HTTP API errors. (7)

**Publisher** – The owner of Dataverse customizations; provides the prefix (`hl_`) and option value prefix. (4)

**QueryExpression** – The SDK for .NET's object model for queries. (4)

**Reconciliation** – Proving that a target matches its source with counts, comparisons and checksums. (16)

**RemoteExecutionContext** – The execution context Dataverse sends to webhooks and Service Bus. (9)

**Replay** – Re-running an orchestrator from its history; also, resending dead-lettered messages. (12, 24)

**Retry-After** – The wait time returned with a throttling error. (15)

**Rolling update** – A Flex Consumption update strategy that replaces instances in batches without downtime. (20)

**Row version** – A Dataverse value that changes on each update of a row. (7)

**RPO / RTO** – Recovery point and recovery time objectives: acceptable data loss and downtime. (25)

**Runbook** – Step-by-step instructions for responding to an alert. (25)

**Scale controller** – The platform component that adds and removes instances. (2)

**Security role** – A set of Dataverse privileges with depths, assigned to users and teams. (5)

**Service endpoint** – A Dataverse registration that posts events to Azure Service Bus. (9, 11)

**Service protection limits** – Dataverse's per-user request, execution-time and concurrency limits. (15)

**Settlement** – Completing, abandoning or dead-lettering a Service Bus message. (11)

**Singleton** – One shared instance: a service registration lifetime, or a timer that runs on one instance. (6, 10)

**Span** – One timed operation within a trace. (18)

**Step** – A Dataverse registration of code or an endpoint on a message, table, stage and mode. (9)

**Structured log** – A log whose values are stored as named properties. (18)

**Subscription (Service Bus)** – A topic's own queue of copies of every message. (11)

**Subscription key (APIM)** – The key that identifies an API Management subscriber. (14)

**Sync hash** – A hash of synchronized fields stored after each sync to detect echoes and conflicts. (21)

**Throttling** – Rejecting requests above a limit, usually with HTTP 429. (15)

**Topic** – A Service Bus entity that copies each message to every subscription. (11)

**Transient failure** – A failure likely to pass on retry, such as a timeout or 503. (24)

**Trigger** – What starts a function; each function has exactly one. (2)

**Typed client** – A class that wraps a configured `HttpClient` with business methods. (8)

**Upsert** – Update a row if it exists, otherwise create it. (4)

**User delegation SAS** – A shared access signature signed with an Entra ID–backed key instead of an account key. (22)

**Watermark** – The point up to which data has been processed. (10)

**Web API** – Dataverse's RESTful OData endpoint. (4)

**Webhook** – An HTTP callback; in Dataverse, a step that posts the execution context to a URL. (9)

**Well-Architected Framework** – Microsoft's guidance in five pillars for reviewing Azure workloads. (25)

**Workload identity federation** – Azure DevOps' equivalent of OIDC for service connections. (20)
