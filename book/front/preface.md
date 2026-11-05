## Preface

Most Dynamics 365 projects reach a point where Dataverse alone isn't enough. A finance system needs invoices, a partner needs an API, a file arrives every night, a process waits days for a signature. Azure Functions is often the right place for that work: close to Microsoft's platform, cheap at rest, able to scale, and written in the C# that Dynamics 365 developers already know.

This book teaches you to build those integrations properly. Not "hello world" functions, but code you can put in front of an architecture review: secret-free authentication, idempotent processing, honest error handling, measured performance, observability, and automated delivery. It follows one fictional company, Harbourline Group, through twenty-five chapters as its integration platform, Harbourline Connect, grows from one endpoint to a production system.

### Who this book is for

You are a Dynamics 365 or Power Platform developer who knows Dataverse (tables, plug-ins, security roles) and C#, and is new or fairly new to Azure. Or you are an Azure developer joining a Dynamics 365 project. Architects will find the design chapters (2, 14, 15, 25) and the capstones useful on their own.

### What you need

A Windows, macOS or Linux computer with the .NET 10 SDK; an Azure subscription; and a Power Platform developer environment, which is free. Chapter 3 lists every tool.

### How the book is organised

- **Part I – Foundations** (Chapters 1–3): when to use Functions with Dynamics 365, how Functions work, and a working environment.
- **Part II – Connecting to Dynamics 365 / Dataverse** (4–6): the APIs, secret-free authentication, and the first deployed function.
- **Part III – Real-World Integration Patterns** (7–13): REST APIs, partner APIs, webhooks, scheduled jobs, Service Bus, Durable Functions and file processing.
- **Part IV – Advanced Enterprise Implementations** (14–20): secure APIs, high volume, migration, Excel, monitoring, testing and DevOps.
- **Part V – Capstone Projects** (21–25): four complete solutions and production readiness.

Each chapter follows the same structure, from the business problem through architecture, step-by-step implementation, configuration, testing, troubleshooting, security, performance and design alternatives, to an exercise, a knowledge check and references. Appendices A–I collect standards, references, a troubleshooting catalogue, a glossary, the answers and interview questions.

### The companion code

Every code listing is pulled automatically from a compiled solution in the companion repository:

> **github.com/vikashkumar669/AzureFunctions**

The caption under each listing names its file. The repository also contains the setup tool that creates the Dataverse schema, mock partner APIs, operator tools, Bicep templates, pipelines and the unit tests. Code is MIT-licensed; use it in your own projects.

### Conventions

- `Code`, file names and settings are in a monospace font; placeholders look like `<your-dev-org>`.
- Numbered markers such as ❶ in listings are explained in the list that follows the listing.
- Callouts mark **tips**, **notes**, **warnings**, **security** and **performance** points.
- Figures show our code in navy, Microsoft services in white and external systems in light blue; dashed boxes are trust boundaries and amber dashed arrows are failure paths.
- "Microsoft Learn" in a chapter's references means the page was checked on the date given.

### How the content was verified

This book was written to a simple rule: say only what has been checked, and say how. Concretely:

- **Platform facts** (limits, defaults, supported features, error codes) were checked against Microsoft Learn in October 2026 and are cited at the end of each chapter. Microsoft changes its platforms often; check the cited page before relying on a number.
- **Code** is compiled and unit-tested by continuous integration on every change (91 tests at the time of writing), and the Bicep templates are compiled the same way. Unit tests prove the code's own logic; they don't prove behaviour against a live Dataverse environment or real partner systems. Run the integration and smoke checks described in Chapters 19 and 20 in your own environments before production use.
- **Screenshots** are shown as clearly labelled placeholders with a capture plan (what to capture, where, and what to annotate), because portal screens change frequently and must be captured from the reader's or author's own tenant. Nothing presented as a screenshot is invented.
- **Performance**: the book publishes no throughput or latency figures. It gives a measurement method and a results template, because numbers from one environment mislead in another.
- **Sample output** marked *illustrative* shows the shape of real output with fictional data.

### A note on names

Harbourline Group, Harbourline Connect, LedgerOne, SignBridge, Northwind Lettings and all people, properties, addresses and data in this book are fictional. Any resemblance to real companies or people is coincidental.

### Acknowledgements

Thanks to the Microsoft documentation teams, whose public, versioned documentation made it possible to check every claim, and to the open-source maintainers of the libraries this book uses.

*Vikash Kumar, October 2026*
