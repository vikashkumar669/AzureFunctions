"""Mock-up specs, Chapters 3–12. Fictional data throughout."""

from kit import (buttons, cards, chip, cmds, dialog, editor, fields, frame,
                 h1, h2, note, panel, table, tabs, term, toggle, two)

FUNC = "func-hlconnect-dev-7k2q"
ORG = "harbourline-dev.crm11.dynamics.com"

AZ_NAV_FUNC = ["Overview", "Activity log", "Access control (IAM)", "#Functions",
               "Functions", "App files", "#Settings", "Environment variables",
               "Configuration", "Authentication", "Identity", "Networking",
               "Scale and concurrency", "#Monitoring", "Application Insights", "Logs"]


def nav_on(items, on):
    return [("*" + i) if i == on else i for i in items]


S = {}

# --- Chapter 3 ----------------------------------------------------------
S["03/core-tools"] = lambda: frame("Terminal — Harbourline.Connect.Functions", "terminal", term(
    "[p]~/HarbourlineConnect/src/Harbourline.Connect.Functions$[/p] func start\n"
    "\n"
    "Azure Functions Core Tools\n"
    "Core Tools Version:       4.x\n"
    "Function Runtime Version: 4.x  {1}\n"
    "\n"
    "[d][2026-10-27T09:14:02.318Z] Found …/Harbourline.Connect.Functions.csproj.[/d]\n"
    "[d][2026-10-27T09:14:05.902Z] Worker process started and initialized. (dotnet-isolated){1}[/d]\n"
    "\n"
    "Functions:\n"
    "\n"
    "        [y]CreateProperty[/y]: [POST] http://localhost:7071/api/v1/properties  {2}\n"
    "        [y]GetProperty[/y]: [GET] http://localhost:7071/api/v1/properties/{code}\n"
    "        [y]Health[/y]: [GET] http://localhost:7071/api/health\n"
    "        [y]ListProperties[/y]: [GET] http://localhost:7071/api/v1/properties\n"
    "        [y]UpdateProperty[/y]: [PATCH] http://localhost:7071/api/v1/properties/{code}\n"
    "\n"
    "        [y]AccountEventsToLedgerOne[/y]: serviceBusTrigger  {3}\n"
    "        [y]NightlyInvoiceSync[/y]: timerTrigger\n"
    "        [y]ProcessOrder[/y]: serviceBusTrigger\n"
    "\n"
    "[d]For detailed output, run func with --verbose flag.[/d]\n"
    "[g][2026-10-27T09:14:07.140Z] Host lock lease acquired by instance ID '0000…'.[/g]"
    ), top=False)

S["03/pp-env"] = lambda: frame("Power Platform admin center", "admin",
    cmds("Edit", "Backup & Restore", "Copy", "Reset", "Delete") +
    h1("Harbourline-Dev") +
    (panel("Details", fields(
        ("Environment ID", "1d3c9e4a-0000-4b7e-9f00-hl-dev-0001", "ro"),
        ("Type", "Developer {1}", "ro"),
        ("State", chip("Ready", "ok"), "ro"),
        ("Region", "United Kingdom", "ro"),
        ("Environment URL", f"{ORG} {{2}}", "ro"),
        ("Organization ID", "7a9b2c1d-0000-4e5f-8a00-hl-org-0001 {3}", "ro"),
        ("Security group", "None", "ro"))) +
        two(panel("Version", fields(("Dataverse", "Enabled", "ro"), ("Database version", "9.2.x", "ro"))),
        panel("Access", fields(("Security roles", "See all", "ro"), ("Teams", "See all", "ro"),
                                 ("Users", "See all", "ro"), ("S2S apps", "See all", "ro"))))),
    crumb="Manage › Environments › Harbourline-Dev",
    nav=["Home", "#Manage", "*Environments", "Environment groups", "Data policies",
         "Capacity", "#Monitor", "Analytics", "Help + support"])

# --- Chapter 4 ----------------------------------------------------------
S["04/keys"] = lambda: frame("Power Apps — Solutions", "maker",
    cmds("+ New key", "Refresh") + h1("Property › Keys") +
    tabs("Columns", "Relationships", "*Keys", "Views", "Forms", "Business rules") +
    table(["Display name", "Name", "Key columns", "Status"], [
        ["Property code key", "hl_propertycode_key {1}", "Property code (hl_propertycode) {2}", "Active {3}"],
    ], selected=0) +
    note("An alternate key is created asynchronously. Status shows Pending, then In progress, then Active.")
    + h2("Key details") + fields(
        ("Display name", "Property code key", "ro"), ("Name", "hl_propertycode_key", "ro"),
        ("Key columns", "Property code", "ro"), ("Status", chip("Active", "ok"), "ro")),
    crumb="Solutions › Harbourline Connect › Tables › Property › Keys",
    nav=["Home", "Create", "Learn", "Apps", "Tables", "Flows", "*Solutions", "More"])

S["04/probe"] = lambda: frame("Terminal — HarbourlineConnect", "terminal", term(
    "[p]~/HarbourlineConnect$[/p] dotnet run --project samples/Harbourline.Probe\n"
    "\n"
    "QueryExpression: 3 rows  {1}\n"
    "  Harbour View Apartment 2 (2 bed)\n"
    "  Kirkgate Mews 14 (3 bed)\n"
    "  Wellington Court 7 (2 bed)\n"
    "FetchXML: 3 rows  {2}\n"
    "  Harbour View Apartment 2 (2 bed)\n"
    "  Kirkgate Mews 14 (3 bed)\n"
    "  Wellington Court 7 (2 bed)\n"
    "Web API: 3 rows  {3}\n"
    "  Harbour View Apartment 2 (2 bed)\n"
    "  Kirkgate Mews 14 (3 bed)\n"
    "  Wellington Court 7 (2 bed)\n"
    "\n"
    "[d]All three queries: city = Leeds, bedrooms >= 2, status = Available; same rows in the same order.[/d]"),
    top=False)

# --- Chapter 5 ----------------------------------------------------------
S["05/identity-blade"] = lambda: frame("Azure portal", "azure",
    h1(f"{FUNC} | Identity") +
    tabs("*System assigned", "User assigned") +
    note("A system assigned managed identity is restricted to one per resource and is tied to the lifecycle of this resource.")
    + cmds("Save {2}", "Discard", "Refresh", "Got feedback?") +
    fields(("Status", toggle(True, "On  ") + "{1}", "ro"),
           ("Object (principal) ID", "5c1e8f20-0000-4d2a-b7c1-hl-mi-0001 {3}", "ro"),
           ("Permissions", "Azure role assignments", "ro")),
    crumb=f"Home › Function App › {FUNC} › Settings › Identity",
    nav=nav_on(AZ_NAV_FUNC, "Identity"))

S["05/app-user"] = lambda: frame("Power Platform admin center", "admin",
    h1("Application users") + cmds("+ New app user", "Refresh") +
    table(["Name", "App ID", "State"], [
        ["hlconnect-dev (managed identity)", "5c1e8f20-…-0001", "Active"]]) + (
        dialog("Create a new app user",
               fields(("App", "hlconnect-dev  [+ Add an app] {1}"),
                      ("Business unit", "harbourline-dev {2}"),
                      ("Security roles", "Harbourline Integration {3}"))
               + buttons("!Create {4}", "Cancel"))),
    crumb="Environments › Harbourline-Dev › Settings › Users + permissions › Application users",
    nav=["Home", "#Manage", "*Environments", "Data policies", "#Settings",
         "Users + permissions", "Product", "Business", "Audit and logs"])

S["05/role"] = lambda: frame("Power Platform admin center", "admin",
    h1("Harbourline Integration") +
    tabs("Core Records", "Marketing", "Sales", "Service", "Business Management",
         "*Custom Tables {1}", "Customization") +
    table(["Table", "Create", "Read", "Write", "Delete", "Append", "Append To"], [
        ["Lease", "Organization", "Organization", "Organization", "None", "Organization", "Organization"],
        ["Order", "Organization", "Organization", "Organization", "None", "Organization", "Organization"],
        ["Property {2}", "Organization", "Organization {3}", "Organization", "None {4}", "Organization", "Organization"],
        ["Sync state", "Organization", "Organization", "Organization", "None", "None", "None"],
        ["Migration checkpoint", "Organization", "Organization", "Organization", "None", "None", "None"],
    ], selected=2) +
    note("Least privilege: the integration role can create, read and update its tables, but never delete."),
    crumb="Settings › Users + permissions › Security roles › Harbourline Integration",
    nav=["Home", "#Manage", "*Environments", "#Settings", "Users + permissions", "Security roles"])

# --- Chapter 6 ----------------------------------------------------------
S["06/fn-list"] = lambda: frame("Azure portal", "azure",
    h1(f"{FUNC}") + tabs("Get started", "Monitoring", "Properties", "Notifications", "*Functions") +
    cmds("Create", "Refresh", "Enable", "Disable", "Delete") +
    table(["Name", "Trigger", "Status"], [
        ["GetProperty {1}", "HTTP {2}", "Enabled {3}"],
        ["Health", "HTTP", "Enabled"],
        ["ListProperties", "HTTP", "Enabled"],
    ], selected=0),
    crumb=f"Home › Function App › {FUNC} › Overview",
    nav=nav_on(AZ_NAV_FUNC, "Overview"))

S["06/fn-key"] = lambda: frame("Azure portal", "azure",
    h1("GetProperty | Function Keys") + cmds("+ New function key", "Refresh") +
    table(["Name", "Value", ""], [
        ["default {1}", "•••••••••••••••••••••••••••••••• {2} Show value", "Copy {3}  Renew  Delete"],
    ], selected=0) +
    note("Function keys are shared secrets. Keep them in Key Vault or a password manager; rotate them by renewing."),
    crumb=f"Home › {FUNC} › Functions › GetProperty › Function Keys",
    nav=["Overview", "Code + Test", "Integration", "Invocations", "Logs", "Metrics", "*Function Keys"])

# --- Chapter 7 ----------------------------------------------------------
S["07/http-file"] = lambda: frame("Visual Studio Code — properties.http", "vscode", editor(
    ["▾ tests", "  ▾ requests", "    properties.http", "▾ src", "▾ samples"], [
        "[c]@base = http://localhost:7071/api/v1[/c]",
        "",
        "[c]### List available properties in Leeds[/c]  {1}",
        "[k]GET[/k] {{base}}/properties?city=Leeds&status=available&minBedrooms=2&pageSize=10",
        "Accept: application/json",
        "",
        "[c]### Update the rent, only if unchanged since we read it[/c]  {2}",
        "[k]PATCH[/k] {{base}}/properties/HBR-0042",
        "Content-Type: application/json",
        "If-Match: \"1874402\"",
        "",
        "{ [s]\"monthlyRent\"[/s]: 1250.00 }",
        "",
        "[c]### Response (shown in the right pane)[/c]  {3}",
        "[c]HTTP/1.1 412 Precondition Failed[/c]",
        "[c]Content-Type: application/problem+json[/c]",
        "[c]{ \"title\": \"Precondition failed\", \"status\": 412,[/c]",
        "[c]  \"detail\": \"The property was changed by someone else.\" }[/c]",
    ], active=2), top=False)

# --- Chapter 8 ----------------------------------------------------------
S["08/retries"] = lambda: frame("Azure portal", "azure",
    h1("End-to-end transaction details") +
    cards(("Operation", "NightlyInvoiceSync"), ("Total duration", "2.84 s {3}"), ("Result", "Success")) +
    table(["Event", "Type", "Result", "Start", "Duration"], [
        ["NightlyInvoiceSync", "Request", "Success", "02:00:00.112", "2.84 s"],
        ["  GET …/invoices?modifiedSince=…", "Dependency", "503 {1}", "+0.31 s", "0.42 s"],
        ["  Retrying in 1.7 s (attempt 2)", "Trace", "—", "+0.74 s", "—"],
        ["  GET …/invoices?modifiedSince=…", "Dependency", "200 {2}", "+2.45 s", "0.29 s"],
        ["  Dataverse UpdateMultiple", "Dependency", "Success", "+2.76 s", "0.08 s"],
    ], html_cells=False, selected=3),
    crumb="Application Insights › Transaction search › NightlyInvoiceSync › End-to-end transaction",
    nav=["Overview", "#Investigate", "Application map", "*Transaction search", "Failures", "Performance", "Logs"])

# --- Chapter 9 ----------------------------------------------------------
S["09/prt-webhook"] = lambda: frame("Plug-in Registration Tool", "dialog", dialog("Register New Web Hook",
    fields(("Name", "hlconnect-lease-status"),
           ("Endpoint URL", f"https://{FUNC}.azurewebsites.net/api/webhooks/lease-status {{1}}"),
           ("Authentication", "WebhookKey ▾ {2}"),
           ("Value", "•••••••••••••••••••••••• (the function key) {3}"))
    + buttons("!Save", "Close")), top=False)

S["09/prt-step"] = lambda: frame("Plug-in Registration Tool", "dialog", dialog("Register New Step",
    fields(("Message", "Update {1}"), ("Primary Entity", "hl_lease {2}"),
           ("Filtering Attributes", "hl_leasestatus {3}"),
           ("Event Handler", "(WebHook) hlconnect-lease-status"),
           ("Event Pipeline Stage", "PostOperation"),
           ("Execution Mode", "Asynchronous {4}"),
           ("Deployment", "Server"),
           ("Delete AsyncOperation if StatusCode = Successful", toggle(True) + "{5}"))
    + buttons("!Register New Step", "Close")), top=False)

S["09/system-jobs"] = lambda: frame("Dynamics 365 — Settings", "d365",
    h1("System Jobs") + cmds("Refresh", "Cancel", "Resume", "Postpone") +
    table(["System Job Name", "Status Reason", "Message", "Started On"], [
        ["hlconnect-lease-status: Update of hl_lease {3}", "Failed {1}",
         "The remote endpoint returned 503 (Service Unavailable). {2}", "27/10/2026 10:42"],
        ["hlconnect-lease-status: Update of hl_lease", "Succeeded", "—", "27/10/2026 10:40"],
        ["hlconnect-lease-status: Update of hl_lease", "Succeeded", "—", "27/10/2026 10:31"],
    ], selected=0),
    crumb="Advanced Settings › System › System Jobs",
    nav=["#System", "Administration", "Auditing", "*System Jobs", "Data Management", "Customizations"])

# --- Chapter 10 ---------------------------------------------------------
S["10/syncstate"] = lambda: frame("Harbourline Admin", "d365",
    h1("Active Sync states") + cmds("+ New", "Refresh", "Export to Excel") +
    table(["Job name {1}", "Token / watermark {2}", "Last run (UTC) {3}"], [
        ["ledgerone-invoices", "2026-10-27T02:00:00.1120000+00:00", "27/10/2026 02:00"],
        ["accounts-to-ledgerone", "918301!10/27/2026 09:15:01", "27/10/2026 09:15"],
        ["ledgerone-to-accounts", "2026-10-27T09:15:30Z", "27/10/2026 09:15"],
    ], selected=0) +
    note("Tokens are stored after each successful batch, so a failed run resumes where the last good one stopped."),
    crumb="Harbourline Admin › Integration › Sync states",
    nav=["#Integration", "*Sync states", "Migration checkpoints", "#Operations", "Orders", "Leases"])

S["10/timer-runs"] = lambda: frame("Azure portal", "azure",
    h1("NightlyInvoiceSync | Invocations") +
    note("Invocation details come from Application Insights and can take a few minutes to appear.") +
    table(["Date (UTC)", "Success", "Result code", "Duration (ms)"], [
        ["2026-10-27 02:00:00 {1}", "✔ Success {2}", "0", "2 841 {3}"],
        ["2026-10-26 02:00:00", "✔ Success", "0", "2 112"],
        ["2026-10-25 02:00:00", "✔ Success", "0", "1 968"],
        ["2026-10-24 02:00:00", "✔ Success", "0", "2 230"],
    ], selected=0),
    crumb=f"Home › {FUNC} › Functions › NightlyInvoiceSync › Invocations",
    nav=["Overview", "Code + Test", "Integration", "*Invocations", "Logs", "Metrics"])

# --- Chapter 11 ---------------------------------------------------------
SB_NAV = ["Overview", "Access control (IAM)", "*Service Bus Explorer", "Properties", "#Settings",
          "Shared access policies", "Subscriptions", "Rules"]

S["11/sb-explorer"] = lambda: frame("Azure portal", "azure",
    h1("ledgerone (account-events/ledgerone) | Service Bus Explorer") +
    tabs("Send messages", "*Receive messages", "Peek from start") +
    two(table(["Sequence", "Message ID", "Delivery count"], [
            ["31", "b0c7…a91e {1}", "1 {2}"], ["32", "f42d…0c37", "1"], ["33", "9a1e…77b2", "1"]],
            selected=0),
        panel("Message properties",
              fields(("…/Claims/EntityLogicalName", "account {3}", "ro"),
                     ("…/Claims/RequestName", "Update", "ro"),
                     ("…/Claims/Organization", ORG, "ro")))) +
    h2("Message body {4}") +
    term('{ "MessageName": "Update", "PrimaryEntityName": "account",\n'
         '  "PrimaryEntityId": "8f1c2a7e-0000-4c11-9a00-hl-acc-1001",\n'
         '  "PostEntityImages": [ … ], "Depth": 1, "Stage": 40 }'),
    crumb="Service Bus namespace › Topics › account-events › Subscriptions › ledgerone › Service Bus Explorer",
    nav=SB_NAV)

S["11/dlq"] = lambda: frame("Azure portal", "azure",
    h1("ledgerone | Service Bus Explorer › Dead-letter") +
    tabs("Active", "*Dead-letter", "Transfer dead-letter") +
    table(["Message ID", "DeadLetterReason {1}", "DeadLetterErrorDescription {2}", "Enqueued time {3}"], [
        ["b0c7…a91e", "PermanentFailureException", "Customer C-1004 was rejected by LedgerOne (illustrative message).",
         "27/10/2026 11:02:14"],
        ["5e21…44c0", "HttpRequestException", "Response status code does not indicate success: 503 (Service Unavailable).",
         "27/10/2026 09:48:51"],
    ], selected=0),
    crumb="… › ledgerone › Service Bus Explorer › Dead-letter",
    nav=SB_NAV)

# --- Chapter 12 ---------------------------------------------------------
S["12/dts-dashboard"] = lambda: frame("Durable Task Scheduler dashboard", "dts",
    h1("Task hub: default › Orchestrations") +
    table(["Instance ID", "Name", "Status", "Created (UTC)"], [
        ["lease-signing-LSE-0042 {1}", "LeaseSigningOrchestration", "Running {2}", "2026-10-27 10:12:03"],
        ["lease-signing-LSE-0039", "LeaseSigningOrchestration", "Completed", "2026-10-25 16:40:11"],
    ], selected=0) +
    h2("History — lease-signing-LSE-0042") +
    table(["Event", "Name", "Timestamp (UTC)"], [
        ["ExecutionStarted", "LeaseSigningOrchestration", "10:12:03.204"],
        ["TaskScheduled", "SendEnvelope", "10:12:03.311"],
        ["TaskCompleted {3}", "SendEnvelope", "10:12:04.020"],
        ["TimerCreated {4}", "fires 2026-11-03 10:12:04 (reminder)", "10:12:04.402"],
        ["(waiting for event)", "SignatureCompleted", "—"],
    ]),
    crumb="http://localhost:8082 › Task hub default › Orchestrations", top=True, search="",
    who="emulator")
