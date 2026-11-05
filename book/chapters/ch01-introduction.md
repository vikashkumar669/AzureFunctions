---
chapter: 1
part: "Part I — Foundations"
title: "Introduction to Azure Functions and Dynamics 365"
build-summary: "Before writing any code, you learn where Azure Functions fits beside Dataverse, plug-ins, Power Automate and Logic Apps, and you make your first architecture decision for Harbourline."
builds:
  - "A map of Harbourline's integration landscape and the eight needs this book solves"
  - "A four-question test for deciding where integration logic belongs"
  - "A decision matrix that assigns each Harbourline requirement to the right tool"
versions: "Concepts only · facts checked against Microsoft Learn on 24 October 2026"
---

::: objectives
- Explain what Azure Functions is and what "serverless" means for cost, scale and operations.
- Describe how Dynamics 365 apps, Dataverse and the Power Platform fit together.
- Say exactly where custom code runs when a Dataverse record is saved, and which limits apply there.
- Choose between a plug-in, Power Automate, Logic Apps, a data link and Azure Functions for a given requirement.
- Name the benefits, limitations and costs of moving integration logic into Azure Functions.
:::

## The business problem

Harbourline Group is a fictional property-management and home-furnishing company that runs Dynamics 365 Sales and Dynamics 365 Customer Service. Over ten years it has grown a tangle of integrations: a console application on a virtual machine copies customers to the finance system every night, a spreadsheet of available properties is exported by hand each morning, and a contractor's script sends leases for signature with a client secret saved in a configuration file.

Each of these works until it doesn't. The nightly job fails silently when the virtual machine restarts. The spreadsheet is a day old by the time tenants see it. The client secret expired last spring, and nobody noticed for three days because there were no alerts.

Harbourline's new integration lead, Priya Raman (a fictional character, like everyone at Harbourline), wants one platform for all of this, owned by the developers who already write Dataverse plug-ins. She has eight requirements, listed in @tbl:needs, and a question: which of them belong in Azure Functions, and which belong somewhere else?

This chapter answers that question. Every later chapter then builds one part of the platform, which the team calls **Harbourline Connect**.

## How it works

### What Azure Functions is

Microsoft describes Azure Functions as "a serverless solution that allows you to build robust apps while using less code, and with less infrastructure and lower costs." You write a function: a method that runs when something happens. That something is the function's **trigger**: an HTTP request, a timer, a message on a queue, a file arriving in storage. Azure supplies the servers, starts as many copies of your code as the load needs, and stops them when the work is done.

Three properties follow from that model, and each shapes the code in this book.

**Event-driven.** A function has exactly one trigger. It runs, does its work and ends. There is no long-running process waiting for input, so state that must survive between runs lives somewhere else: in Dataverse, in storage, or in a Durable Functions orchestration (Chapter 12).

**Elastic.** On the Flex Consumption plan, which Microsoft recommends for new serverless apps, the platform adds instances as events arrive and can scale back to zero when there is nothing to do. Your code must therefore assume that many copies of it run at once.

**Pay for what runs.** On demand, Flex Consumption bills the memory of instances while they are executing functions, measured in gigabyte-seconds, plus the number of executions, each after a monthly free grant. Optional **always-ready** instances remove start-up delay but add a baseline charge with no free grant. Check the current rates on the Azure Functions pricing page before you estimate costs; this book doesn't quote prices because they change.

Functions can be written in C#, Java, JavaScript, PowerShell, Python and Go. This book uses C# on .NET 10 with the **isolated worker model**, the only .NET model supported for new apps after 10 November 2026 (Chapter 2 explains why).

### What Dynamics 365 and Dataverse are

**Dataverse** is the data platform underneath the Power Platform. Microsoft's definition: "Dataverse lets you securely store and manage data that's used by business applications." Data lives in **tables** made of **rows** and **columns**. Access is controlled by **security roles**, so a user, or an application, sees only the rows it has been granted.

**Dynamics 365** apps such as Sales and Customer Service are built on Dataverse. In Microsoft's words, these apps "use Dataverse to store and secure the data they use." When a salesperson creates an account in Dynamics 365 Sales, they are creating a row in Dataverse's `account` table. That is why this book talks about Dataverse more than about any one Dynamics 365 app: integrating with Dataverse *is* integrating with Dynamics 365.

Developers extend Dataverse in three main ways:

- **Plug-ins**: .NET code that Dataverse runs inside its own event pipeline when a message such as Create or Update is processed.
- **The Dataverse SDK for .NET and the Web API**: ways for code running elsewhere to read and write data. Chapter 4 compares them.
- **Event publishing**: Dataverse can push a description of an event to an external endpoint, either over HTTP (a **webhook**) or to Azure Service Bus or Event Hubs (a **service endpoint**).

### Why integrations exist

Dataverse is the system of record for Harbourline's customers and properties, but it is not the only system the business runs. Finance lives in an ERP; contracts are signed in an e-signature service; property data arrives as files from a partner; tenants use a portal; analysts want everything in a reporting store. An **integration** is the code and configuration that moves data or events between those systems reliably, securely, and without people copying spreadsheets.

@fig:landscape shows Harbourline's landscape. Harbourline Connect sits between Dataverse and everything else. That is a deliberate design choice: Dataverse stays the clean system of record, and integration logic lives in one place that can be tested, monitored and deployed on its own.

![Harbourline Connect sits between Dataverse and five external systems and teams. Every arrow on the right is built in a later chapter.](figures/ch01/landscape.svg){#fig:landscape}

### Where code runs when a record is saved

If you already write plug-ins, the most important difference between a plug-in and a function is *where* it runs. @fig:pipeline shows the Dataverse event pipeline for a single save.

![Synchronous plug-ins run inside the Dataverse transaction and can stop the save; Azure Functions run outside it, after the commit, with their own scale and time limits.](figures/ch01/pipeline.svg){#fig:pipeline}

The pipeline has four stages around the platform's own operation. Plug-ins registered in the PreOperation and PostOperation stages run inside the **database transaction**: if one throws an exception, the whole save is rolled back. That is the plug-in's superpower, and the reason some logic can only be a plug-in.

The price of that power is a strict time budget. Microsoft documents that a Dataverse message operation "must complete within 2 minutes", including its registered plug-ins; beyond that, Dataverse throws a `TimeoutException` and rolls the operation back. Microsoft also advises keeping a plug-in's own execution under two seconds, and registering anything slower to run asynchronously.

Azure Functions run outside this pipeline. Dataverse hands the event to a function in one of two ways:

- **Webhook**: Dataverse sends an HTTP POST with a JSON description of the event, a `RemoteExecutionContext`. Webhook steps can be synchronous or asynchronous. Dataverse waits at most 60 seconds for a response, and a synchronous failure is shown to the user.
- **Azure Service Bus**: Dataverse posts the same context to a queue or topic, and a function reads it. Service Bus steps are always asynchronous. Microsoft notes that Service Bus "works for high scale processing, and provides a full queuing mechanism", while webhooks "can only scale to the point at which your hosted web service can handle the messages."

Asynchronous steps run after the commit, through Dataverse's asynchronous service, and their outcome is recorded as a **system job**. The user has already moved on. That is ideal for slow work, and it is the pattern this book uses most.

## Architecture

The eight requirements in @tbl:needs come from Priya's backlog. Each is solved in a later chapter, and together they make up Harbourline Connect.

::: {#tbl:needs caption="Harbourline's eight integration needs and where this book solves them"}
| # | Need | Trigger | Chapters |
|---|---|---|---|
| 1 | Tenant portal reads available properties in real time | HTTP request | 6, 7, 14, 23 |
| 2 | New and changed customers reach LedgerOne ERP | Dataverse event | 8, 11, 21 |
| 3 | Signed leases from SignBridge update Dataverse | Webhook from SignBridge | 8, 9, 12 |
| 4 | Nightly delta sync of invoices from LedgerOne | Timer | 10 |
| 5 | A partner's property files are validated and imported | File in Blob Storage | 13, 15, 16 |
| 6 | Orders flow to fulfilment, surviving outages | Service Bus message | 11, 24 |
| 7 | Finance exports lease packs and imports price lists in Excel | HTTP request | 17, 22 |
| 8 | Lease activation waits for signature and deposit | Durable orchestration | 12 |
:::

Every one of these needs to authenticate to Dataverse, log what it does, respect Dataverse's limits and deploy through a pipeline. Those cross-cutting concerns are Chapters 5, 15, 18 and 20.

## Prerequisites

This chapter has no software prerequisites. You need:

- Working knowledge of C# and .NET.
- Basic familiarity with Dataverse tables, columns and security roles, and with what a plug-in is.

Chapter 3 lists everything to install before the first code chapter.

## Step-by-step: deciding where logic belongs

Most integration mistakes are not coding mistakes; they are placement mistakes. A slow external call inside a synchronous plug-in, a business-critical sync built in a flow that nobody monitors, a reporting copy rebuilt by hand in code: each is a design error before it is a bug. @fig:decision is a four-question test that catches them early.

![Four questions, asked in order, decide whether logic belongs in a plug-in, a data link, Power Automate, Logic Apps or Azure Functions.](figures/ch01/decision.svg){#fig:decision}

### Step 1 – Ask whether it must block or roll back the save

If a rule must be enforced *before* the record is committed (a duplicate VAT number, a required value calculated from other columns), only a synchronous plug-in can enforce it. Keep the plug-in thin: validate, set values, and hand anything slow or external to an asynchronous step.

### Step 2 – Rule out a copy of data

If the goal is analytics, reporting or a data warehouse, you probably need no integration code at all. **Link to Microsoft Fabric** gives a no-copy, no-ETL path into Fabric, and **Azure Synapse Link for Dataverse** exports data to your own storage. Microsoft states that both services remain supported. The older Export to Data Lake service is being stopped permanently on 30 November 2026.

### Step 3 – Decide who will own and change it

If business users will build and change the logic (an approval, a notification, a simple copy between Microsoft 365 services), **Power Automate** is the right home. Makers can see, change and run it without a developer. A flow can still call an Azure Function for the hard part, through a custom connector (Chapter 9).

### Step 4 – Separate orchestration from code

If the logic is an IT-owned workflow across many SaaS or business-to-business systems, **Azure Logic Apps** offers more than 1,400 prebuilt connectors and a visual designer. Logic Apps and Functions combine well: a workflow can call functions, and functions can start workflows.

Everything else lands in **Azure Functions**: custom APIs, heavy or high-volume processing, scheduled jobs, file processing, long-running orchestrations, and anything that needs real code, tests and a deployment pipeline.

### Step 5 – Record the decision

Apply the questions to every requirement and record the result. @tbl:matrix is Harbourline's decision matrix for the eight needs and for three rules from the same backlog that stay in Dataverse or Power Automate.

::: {#tbl:matrix caption="Harbourline's decision matrix"}
| Requirement | Q1 block save? | Q2 data copy? | Q3 maker-owned? | Q4 IT workflow? | Decision |
|---|---|---|---|---|---|
| Reject duplicate VAT numbers | Yes | | | | Synchronous plug-in |
| Weekly occupancy dashboard | No | Yes | | | Fabric link |
| Notify a manager when a lease is approved | No | No | Yes | | Power Automate |
| Needs 1–8 in @tbl:needs | No | No | No | No | Azure Functions |
:::

Notice that the plug-in and the function often work as a pair. The plug-in rejects bad data inside the transaction; an asynchronous step then hands the clean record to a function for everything slow.

## Benefits, limitations and costs

Moving integration logic into Azure Functions is not free. @tbl:tradeoffs sets out what Harbourline gains and what it takes on.

::: {#tbl:tradeoffs caption="What Azure Functions gives and what it costs"}
| Area | Benefit | Limitation or cost |
|---|---|---|
| Time | Flex Consumption has a 30-minute default timeout and no enforced maximum | An HTTP-triggered function must respond within 230 seconds; longer work needs Durable Functions or an asynchronous pattern |
| Scale | Instances are added as events arrive | Many copies run at once, so code must be stateless and idempotent |
| Transactions | Slow work never holds up a user's save | An asynchronous function cannot roll back the save, so data is eventually consistent; a synchronous webhook can, but the user waits and the outbound call can't be recalled |
| Libraries and network | Any NuGet package, virtual network integration, private endpoints | You patch and upgrade dependencies yourself |
| Start-up | Scales to zero when idle | The first request after idle waits for an instance; always-ready instances remove that delay at a baseline cost |
| Operations | Application Insights, alerts and CI/CD from day one | Someone must own monitoring, alerts and on-call |
| Cost | Pay for execution, with a monthly free grant on demand | Always-ready instances, storage, Service Bus and monitoring all have their own charges |
:::

## Common misconceptions

::: {#tbl:myths caption="Misconceptions that cause real incidents"}
| Belief | Reality |
|---|---|
| "A function can reject a bad save, like a plug-in." | Only a synchronous plug-in or synchronous webhook can stop the save; an asynchronous function sees the record after it is committed. |
| "Serverless means no limits." | Dataverse still limits each user, including your function's application user (Chapter 15), and HTTP responses are capped at 230 seconds. |
| "Power Automate and Functions are rivals." | They work together: a flow can call a function for the parts that need code. |
| "A function needs a client secret to reach Dataverse." | A managed identity lets a function authenticate with no secret at all (Chapters 5 and 6). |
| "Plug-in managed identity lets Azure call Dataverse." | It lets plug-ins call Azure resources such as Key Vault. Azure calling Dataverse uses an application user. |
:::

## Security considerations

::: security
A plug-in runs as a Dataverse user. A function runs outside Dataverse, so it needs an identity of its own: a managed identity registered as an **application user** with a least-privilege security role. Never give an integration the System Administrator role, and never store a Dataverse secret in code or app settings. Chapter 5 covers this in full.
:::

## Performance and scalability

::: performance
Dataverse service protection limits apply per user, and your function's application user is a user. By default each web server allows 6,000 requests, 20 minutes of combined execution time and 52 concurrent requests per user in a five-minute sliding window. A function that scales out to many instances reaches these limits quickly unless it batches and backs off (Chapter 15).
:::

## Alternatives and design decisions

@tbl:tools summarises the alternatives this chapter compared.

::: {#tbl:tools caption="Where each tool fits"}
| Tool | Best for | Owned by | Runs |
|---|---|---|---|
| Synchronous plug-in | Validation and values that must be set before commit | Developers | Inside the Dataverse transaction |
| Asynchronous plug-in | Light follow-up work inside Dataverse | Developers | After commit, inside Dataverse |
| Power Automate | Approvals, notifications, maker-owned automation | Makers | Microsoft cloud |
| Azure Logic Apps | IT-owned workflows across many connectors, B2B | IT and integration teams | Azure |
| Fabric link / Synapse Link | Analytics and reporting copies | Data teams | Managed service |
| Azure Functions | Custom APIs, heavy or high-volume work, files, schedules, orchestration | Developers | Azure, outside Dataverse |
:::

Two newer options are worth knowing. Dataverse plug-ins can now use a **Power Platform managed identity**, generally available, to call Azure resources such as Key Vault without credentials. And **Custom APIs** let you define your own Dataverse messages, which a plug-in, a flow or a function can implement or call (Chapter 9).

## Best practices

- Ask the four questions in @fig:decision before writing any integration code.
- Keep plug-ins thin; register slow work as asynchronous steps that hand off to functions.
- Prefer asynchronous handoff (Service Bus or async webhook) over synchronous calls from Dataverse.
- Give every integration its own identity and least-privilege role.
- Design every function to run many times in parallel and to tolerate the same event twice.
- Treat Harbourline Connect as a product: version it, test it, monitor it and deploy it through a pipeline.

## Hands-on exercise

::: exercise
**Goal:** apply the four-question test to your own work.

**Steps:** list five integration requirements from a project you know. For each, answer the four questions in @fig:decision and record the result in a table shaped like @tbl:matrix.

**Done when:** every requirement has a decision and one sentence explaining it.

**Stretch:** find a requirement that needs two tools working together, such as a plug-in that validates and a function that does the slow work, and draw where the handoff happens on a copy of @fig:pipeline.
:::

## Knowledge check

::: knowledge-check
1. A rule must stop a user from saving an account with a duplicate VAT number. Where does it belong, and why?
2. What happens when a Dataverse message operation, including its plug-ins, runs longer than two minutes?
3. Name two differences between delivering Dataverse events by webhook and by Azure Service Bus.
4. What two things does Flex Consumption bill for on demand?
5. Why must a function that writes to Dataverse be designed to run many times in parallel?
:::

Answers are in Appendix G.

::: summary
- Azure Functions runs event-driven code without servers to manage, scales out automatically and bills for execution.
- Dynamics 365 apps store their data in Dataverse, so integrating with Dataverse is integrating with Dynamics 365.
- Synchronous plug-ins run inside the Dataverse transaction under a two-minute limit; functions run outside it, after the commit.
- Four questions decide where logic belongs: block the save, copy the data, maker-owned, or IT workflow; everything else is a function.
- Plug-ins, flows, Logic Apps and functions work best together, each doing the part it is best at.
:::

## References

- [Azure Functions overview](https://learn.microsoft.com/azure/azure-functions/functions-overview) – Microsoft Learn, checked 2026-10-24.
- [Azure Functions hosting options](https://learn.microsoft.com/azure/azure-functions/functions-scale) – Microsoft Learn, checked 2026-10-23.
- [Estimating consumption-based costs](https://learn.microsoft.com/azure/azure-functions/functions-consumption-costs) – Microsoft Learn, checked 2026-10-24.
- [Azure Functions pricing](https://azure.microsoft.com/pricing/details/functions/) – Microsoft Azure.
- [What is Microsoft Dataverse?](https://learn.microsoft.com/power-apps/maker/data-platform/data-platform-intro) – Microsoft Learn, checked 2026-10-24.
- [Analyze plug-in performance](https://learn.microsoft.com/power-apps/developer/data-platform/analyze-performance) – Microsoft Learn, checked 2026-10-24.
- [Use webhooks to create external handlers for server events](https://learn.microsoft.com/power-apps/developer/data-platform/use-webhooks) – Microsoft Learn, checked 2026-10-24.
- [Azure integration](https://learn.microsoft.com/power-apps/developer/data-platform/azure-integration) – Microsoft Learn, checked 2026-10-24.
- [Service protection API limits](https://learn.microsoft.com/power-apps/developer/data-platform/api-limits) – Microsoft Learn, checked 2026-10-24.
- [Compare Azure Functions, Logic Apps, Power Automate and WebJobs](https://learn.microsoft.com/azure/azure-functions/functions-compare-logic-apps-ms-flow-webjobs) – Microsoft Learn, checked 2026-10-24.
- [Azure Synapse Link and Fabric link transition FAQ](https://learn.microsoft.com/power-apps/maker/data-platform/azure-synapse-link-transition-faq) – Microsoft Learn, checked 2026-10-24.
- [Power Platform managed identity overview](https://learn.microsoft.com/power-platform/admin/managed-identity-overview) – Microsoft Learn, checked 2026-10-24.

<!-- Glossary terms introduced in this chapter: trigger, always-ready,
isolated worker model, Dataverse, table, row, column, security role,
Dynamics 365, plug-in, webhook, service endpoint, integration, database
transaction, system job, Harbourline Connect, Link to Microsoft Fabric,
Azure Synapse Link for Dataverse, Power Automate, Azure Logic Apps,
application user, Power Platform managed identity, Custom API -->
