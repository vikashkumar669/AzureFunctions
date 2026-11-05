"""Mock-up specs, Chapters 13–25. Fictional data throughout."""

from kit import (bars, buttons, cards, chip, cmds, dialog, editor, fields,
                 frame, gantt, grid, h1, h2, note, panel, table, tabs, term,
                 toggle, two)
from specs_a import FUNC, ORG, AZ_NAV_FUNC, nav_on

S = {}

# --- Chapter 13 ---------------------------------------------------------
S["13/eg-sub"] = lambda: frame("Azure portal", "azure",
    h1("Create Event Subscription") +
    tabs("*Basic", "Filters", "Additional features", "Delivery properties", "Advanced editor") +
    h2("Event subscription details") +
    fields(("Name", "hlconnect-feeds"), ("Event schema", "Event Grid Schema")) +
    h2("Event types") +
    fields(("Filter to event types", "Blob Created {1}")) +
    h2("Endpoint details") +
    fields(("Endpoint type", "Web Hook {2}"),
           ("Endpoint", f"https://{FUNC}.azurewebsites.net/runtime/webhooks/blobs?functionName=Host.Functions.ImportPropertyFeed&code=•••••")) +
    panel("Filters tab", fields(("Enable subject filtering", toggle(True)),
                                ("Subject begins with", "/blobServices/default/containers/property-feeds/ {3}"),
                                ("Subject ends with", ".csv"))) +
    buttons("!Create", "Cancel"),
    crumb="Storage account › Events › Event Subscriptions › Create",
    nav=["Overview", "Activity log", "*Events", "#Data storage", "Containers", "File shares", "Queues"])

S["13/report"] = lambda: frame("Azure portal", "azure",
    h1("partner-a-2026-10-27.csv.report.csv") + cmds("Save", "Discard", "Download", "Refresh") +
    tabs("Overview", "Versions", "*Edit", "Generate SAS") +
    editor(["…report.csv"], [
        "line,code,reason",
        "3 {1},,\"Unreadable row: The conversion cannot be performed.\" {3}",
        "4,\"hbr-0103\" {2},\"code must look like ABC-1234.\"",
        "5,\"HBR-0101\",\"Duplicate code in this feed.\"",
    ]),
    crumb="Storage account › Containers › feed-reports › partner-a-2026-10-27.csv.report.csv › Edit",
    nav=["Overview", "#Data storage", "*Containers", "File shares", "Queues", "Tables"])

# --- Chapter 14 ---------------------------------------------------------
S["14/app-roles"] = lambda: frame("Microsoft Entra admin center", "entra",
    h1("Harbourline Partner API | App roles") + cmds("+ Create app role", "Got feedback?") +
    table(["Display name", "Description", "Allowed member types", "Value", "State"], [
        ["Submit orders", "Partners can submit orders", "Applications {3}", "Orders.Submit {1}", "Enabled"],
        ["Read properties", "Partners can read the property catalogue", "Applications", "Properties.Read {2}", "Enabled"],
    ], selected=0),
    crumb="Home › App registrations › Harbourline Partner API › App roles",
    nav=["Overview", "Quickstart", "#Manage", "Branding & properties", "Authentication",
         "Certificates & secrets", "Token configuration", "API permissions", "Expose an API", "*App roles", "Owners"])

S["14/easyauth"] = lambda: frame("Azure portal", "azure",
    h1("Edit identity provider") +
    fields(("Identity provider", "Microsoft", "ro"),
           ("Application (client) ID", "a71c…-0000-4f2b-hl-api-0001"),
           ("Issuer URL", "https://login.microsoftonline.com/<tenant-id>/v2.0"),
           ("Allowed token audiences", "api://harbourline-partner-api")) +
    h2("Additional checks") +
    fields(("Client application requirement", "Allow requests from specific client applications {3}"),
           ("Allowed client applications", "3f0b…-northwind-lettings, 9d2e…-acme-relocation")) +
    h2("App Service authentication settings") +
    fields(("Restrict access", "Require authentication {1}"),
           ("Unauthenticated requests", "HTTP 401 Unauthorized: recommended for APIs {2}")) +
    buttons("!Save", "Cancel"),
    crumb=f"Function App › {FUNC} › Settings › Authentication › Microsoft provider › Edit",
    nav=nav_on(AZ_NAV_FUNC, "Authentication"))

S["14/apim-test"] = lambda: frame("Azure portal", "azure",
    h1("SearchProperties") + tabs("Design", "Settings", "*Test", "Revisions", "Change log") +
    fields(("Request URL", "https://apim-hlconnect.azure-api.net/partner/v1/search/properties?city=Leeds", "ro")) +
    h2("Headers") +
    table(["Name", "Value"], [
        ["Authorization {1}", "Bearer eyJ0eXAiOiJKV1Qi… (token for Northwind Lettings)"],
        ["Ocp-Apim-Subscription-Key {2}", "•••••••••••••••• (Northwind Lettings)"],
    ]) + buttons("!Send", "Trace {4}") +
    h2("HTTP response") +
    term("[g]HTTP/1.1 200 OK[/g] {3}\ncontent-type: application/json; charset=utf-8\n"
         "x-correlation-id: 4c2b9d0e5f1a4e7c\n\n{ \"items\": [ { \"code\": \"HBR-0042\", … } ], \"next\": null }"),
    crumb="API Management › APIs › Harbourline Partner API › SearchProperties › Test",
    nav=["Overview", "#APIs", "*APIs", "Products", "Subscriptions", "Named values", "Backends"])

# --- Chapter 15 ---------------------------------------------------------
S["15/bulk-logs"] = lambda: frame("Azure portal", "azure",
    h1("Logs") +
    editor(["Query 1"], [
        "[lnk]traces[/lnk]",
        "| [k]where[/k] message [k]startswith[/k] [s]\"Bulk load\"[/s]",
        "| [k]project[/k] timestamp, batch = [h]toint[/h](customDimensions.BatchSize),",
        "    parallel = [h]toint[/h](customDimensions.Parallelism),",
        "    rows_s = [h]todouble[/h](customDimensions.RowsPerSecond),",
        "    failed = [h]toint[/h](customDimensions.Failed)",
    ]) +
    table(["timestamp", "batch {1}", "parallel {2}", "rows_s {3}", "failed {4}"], [
        ["2026-10-27 03:10", "100", "4", "(measured)", "0"],
        ["2026-10-27 03:25", "500", "4", "(measured)", "2"],
        ["2026-10-27 03:40", "500", "8", "(measured)", "0"],
    ]) +
    note("Rows-per-second values are left as (measured) on purpose: record your own environment's results, as Chapter 15 explains."),
    crumb="Application Insights › Logs (query from Chapter 18)",
    nav=["Overview", "#Monitoring", "Alerts", "Metrics", "*Logs", "Workbooks"])

S["15/api-analytics"] = lambda: frame("Power Platform admin center", "admin",
    h1("Dataverse analytics › API calls statistics") +
    two(panel("Requests by user {1}", table(["User", "Requests"], [
            ["hlconnect-dev (application)", "(your data)"], ["lettings.agent@harbourline.example", "(your data)"]])
            + bars([40, 62, 55, 90, 70, 48, 30], ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"])),
        panel("Throttled requests {2}", bars([0, 0, 3, 9, 1, 0, 0], ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"], alt={2, 3, 4})
              + note("Bars show the shape of the report only; values are illustrative."))),
    crumb="Manage › Analytics › Dataverse › API calls statistics",
    nav=["Home", "#Manage", "Environments", "*Analytics", "Capacity"])

# --- Chapter 16 ---------------------------------------------------------
S["16/migrate-run"] = lambda: frame("Terminal — HarbourlineConnect", "terminal", term(
    "[p]~/HarbourlineConnect$[/p] dotnet run --project samples/Harbourline.Ops -- \\\n"
    "    migrate legacy/properties.csv properties-2026-10\n"
    "\n"
    "[d](log lines omitted)[/d]\n"
    "Source rows: 8412\n"
    "Migrated:    8409  {2}\n"
    "Resumed after batch: 12  {1}\n"
    "  [y]HBR-0731: Bedrooms or rent is not a number.[/y]  {3}\n"
    "  [y]HBR-2210: Unknown state 'X'.[/y]\n"
    "  [y]HBR-5518: Unknown state 'Q'.[/y]"), top=False)

S["16/reconcile"] = lambda: frame("Terminal — HarbourlineConnect", "terminal", term(
    "[p]~/HarbourlineConnect$[/p] dotnet run --project samples/Harbourline.Ops -- \\\n"
    "    reconcile legacy/properties.csv\n"
    "\n"
    "Source 8409, target 8409  {1}\n"
    "Missing:   0  {2}\n"
    "Different: 0\n"
    "Checksums: 9f3a1c07be42 / 9f3a1c07be42  {3}\n"
    "[g]MATCH[/g]  {4}"), top=False)

# --- Chapter 17 ---------------------------------------------------------
def _inv_cls(r, c):
    if r == 3: return "hdr"
    if c == 3 and r > 3: return "num"
    return ""

S["17/export-xlsx"] = lambda: frame("Microsoft Excel — invoices-2026-09.xlsx", "excel", grid(
    ["A", "B", "C", "D", "E", "F"], [
        ["Harbourline invoices – 2026-09 {1}", "", "", "", "", ""],
        ["", "", "", "", "", ""],
        ["Invoice ▾ {2}", "Lease ▾", "Property ▾", "Amount ▾ {3}", "Due date ▾", "Status ▾"],
        ["INV-2026-0911", "LSE-0042", "Harbour View Apartment 2", "1,250.00", "2026-10-01", "Open"],
        ["INV-2026-0912", "LSE-0043", "Kirkgate Mews 14", "985.50", "2026-10-01", "Open"],
        ["INV-2026-0913", "LSE-0051", "Wellington Court 7", "2,100.00", "2026-10-01", "Paid"],
        ["INV-2026-0914", "LSE-0057", "Mill Lane 3", "950.00", "2026-10-01", "Open"],
    ], _inv_cls, ["Invoices"], 0, ("D4", "1250"))
    + note("Rows 1–3 are frozen {4}: the title and header stay visible while scrolling."), top=False)

def _price_cls(r, c):
    if r == 1: return "hdr"
    if c in (0, 1): return "lock"
    if c == 2: return "edit num"
    return ""

S["17/template-xlsx"] = lambda: frame("Microsoft Excel — prices-template.xlsx", "excel", grid(
    ["A", "B", "C"], [
        ["Property code", "Current rent", "New rent"],
        ["HBR-0042 {1}", "1,250.00", "1,295.00 {2}"],
        ["HBR-0043", "985.50", ""],
        ["HBR-0051", "2,100.00", "twelve hundred"],
    ], _price_cls, ["Prices"], 0, ("C4", "twelve hundred"))
    + dialog("Microsoft Excel {3}", "This value doesn't match the data validation restrictions defined for this cell."
             + buttons("!Retry", "Cancel", "Help")), top=False)

# --- Chapter 18 ---------------------------------------------------------
S["18/e2e"] = lambda: frame("Azure portal", "azure",
    h1("End-to-end transaction details") +
    gantt([("ProcessOrder {1}", 0, 100, False),
           ("Dataverse: Retrieve hl_order {2}", 3, 10, False),
           ("LedgerOne: POST /orders (503)", 15, 12, True),
           ("LedgerOne: POST /orders (200) {3}", 52, 14, False),
           ("Dataverse: Update hl_order", 70, 9, False)]) +
    h2("Traces and events {4}") +
    table(["Time", "Message", "OrderNumber"], [
        ["10:31:07.120", "Processing order", "ORD-10077"],
        ["10:31:07.640", "LedgerOne returned 503; retrying", "ORD-10077"],
        ["10:31:09.300", "Order accepted by LedgerOne", "ORD-10077"],
    ]),
    crumb="Application Insights › Transaction search › ProcessOrder",
    nav=["Overview", "#Investigate", "Application map", "*Transaction search", "Failures", "Performance"])

S["18/workbook"] = lambda: frame("Azure portal", "azure",
    h1("Harbourline operations") + cmds("Edit", "Refresh", "Time range: Last 24 hours") +
    two(panel("Failures per function {1}", table(["Function", "Failures"], [
            ["ProcessOrder", "3"], ["ImportPropertyFeed", "1"], ["LeaseStatusWebhook", "0"]])),
        panel("Dataverse latency (p95) {2}", bars([40, 42, 38, 61, 44, 39, 41, 43],
              ["00", "03", "06", "09", "12", "15", "18", "21"]))) +
    two(panel("Dead-letter depth {3}", table(["Entity", "Messages"], [
            ["order-intake", "1"], ["account-events/ledgerone", "2"]])),
        panel("Last scheduled runs {4}", table(["Job", "Last success (UTC)"], [
            ["NightlyInvoiceSync", "2026-10-27 02:00"], ["PushCustomersToLedgerOne", "2026-10-27 10:30"]]))) +
    note("Values shown are illustrative; the workbook queries are in infra/monitoring/queries.kql."),
    crumb="Resource group › Workbooks › Harbourline operations",
    nav=["Overview", "#Monitoring", "Alerts", "*Workbooks", "Dashboards"])

# --- Chapter 19 ---------------------------------------------------------
S["19/ci-run"] = lambda: frame("GitHub — Actions", "github",
    h1("Import book and companion code #18") +
    cards(("Status", "Success"), ("Total duration", "1m 6s"), ("Artifacts", "0")) +
    panel("azure-functions-book.yml", table(["Job", "Result"], [["build {1}", "✔ 1m 2s"]])) +
    h2("Annotations") +
    table(["", "Message"], [
        [chip("notice"), "Tests: 91 passed, 0 failed, 0 skipped {2}"],
        [chip("notice"), "Bicep: infra/main.bicep and infra/apim.bicep compiled {3}"],
        [chip("notice", "warn"), "Outdated: a newer version of a package is available {4}"],
    ], html_cells=True),
    crumb="vikashkumar669/AzureFunctions › Actions › azure-functions-book › run #18", search="Type / to search",
    who="vikashkumar669",
    nav=["#Actions", "All workflows", "*azure-functions-book", "azure-functions-deploy", "#Management", "Caches", "Runners"])

S["19/test-explorer"] = lambda: frame("Visual Studio Code — Testing", "vscode", editor(
    ["TESTING", "▾ Harbourline.Connect.UnitTests {1}", "  ▸ BulkWriterTests", "  ▾ ConflictResolverTests",
     "      ✔ EqualSides_AreSkipped", "      ✔ BothChanged_MergesBy…", "  ▾ OrderTests",
     "    ▾ ✔ Failures_AreClassified (8) {2}", "        ✔ PermanentFailure…", "        ✔ HttpRequest… 503",
     "        ✔ HttpRequest… 400", "    ✔ InvalidOrder_IsPermanent", "▷ Run  ⟳ Debug {3}"], [
        "[k]public sealed class[/k] [h]OrderTests[/h]",
        "{",
        "    [[h]Theory[/h]]",
        "    [[h]MemberData[/h]([k]nameof[/k](Failures))]",
        "    [k]public void[/k] [h]Failures_AreClassified[/h](Exception ex, [k]bool[/k] transient) =>",
        "        Assert.Equal(transient, FailureClassifier.IsTransient(ex));",
        "",
        "    [k]public static[/k] TheoryData<Exception, [k]bool[/k]> [h]Failures[/h]() => [k]new[/k]()",
        "    {",
        "        { [k]new[/k] PermanentFailureException([s]\"bad\"[/s]), [k]false[/k] },",
        "        { [k]new[/k] HttpRequestException([s]\"down\"[/s], [k]null[/k],",
        "            HttpStatusCode.ServiceUnavailable), [k]true[/k] },",
        "        [c]// …six more cases[/c]",
        "    };",
        "}",
    ], active=7, side=290), top=False)

# --- Chapter 20 ---------------------------------------------------------
S["20/fedcred"] = lambda: frame("Microsoft Entra admin center", "entra",
    h1("hlconnect-deploy-prod | Certificates & secrets") +
    tabs("Certificates (0)", "Client secrets (0) {3}", "*Federated credentials (1)") +
    table(["Name", "Issuer", "Subject identifier"], [
        ["github-prod", "https://token.actions.githubusercontent.com {1}",
         "repo:vikashkumar669/AzureFunctions:environment:prod {2}"]], selected=0) +
    note("No client secrets: GitHub Actions exchanges its OIDC token for an Entra token at run time."),
    crumb="App registrations › hlconnect-deploy-prod › Certificates & secrets › Federated credentials",
    nav=["Overview", "#Manage", "Authentication", "*Certificates & secrets", "API permissions", "Owners"])

S["20/approval"] = lambda: frame("GitHub — Actions", "github",
    h1("azure-functions-deploy #7") +
    gantt([("build", 0, 30, False), ("deploy (dev)", 32, 20, False), ("deploy (prod)", 55, 0.5, False)]) +
    panel("Review pending deployments", chip("Waiting for review", "warn") + " {1}" +
          fields(("Environment", "prod", "ro"), ("Required reviewers {2}", "vikashkumar669", "ro"),
                 ("Comment", "Smoke tests passed in dev."))
          + buttons("!Approve and deploy {3}", "Reject")),
    crumb="vikashkumar669/AzureFunctions › Actions › azure-functions-deploy › run #7", search="Type / to search",
    who="vikashkumar669")

S["20/siteupdate"] = lambda: frame("Azure portal", "azure",
    h1(f"{FUNC} | Configuration") + tabs("General settings", "*Site update strategy") +
    fields(("Update strategy", "RollingUpdate {1}"),
           ("Applies to", "Code deployments and configuration changes", "ro")) +
    note("With a rolling update, instances are replaced in batches, so in-flight executions can finish. Check regional availability before relying on it.")
    + buttons("!Apply", "Discard"),
    crumb=f"Function App › {FUNC} › Settings › Configuration › Site update strategy",
    nav=nav_on(AZ_NAV_FUNC, "Configuration"))

# --- Chapter 21 ---------------------------------------------------------
S["21/account-sync"] = lambda: frame("Dynamics 365 — Sales Hub", "d365",
    cmds("Save", "Save & Close", "+ New", "Deactivate", "Refresh") +
    h1("Northwind Lettings (fictional)") +
    tabs("Summary", "Details", "*Sync", "Related") +
    panel("Synchronization", fields(
        ("Account number", "C-1001 {1}"),
        ("Sync hash", "3b9f0c2a7d41e8f6 {2}", "ro"),
        ("Last synced from", "LedgerOne {3}", "ro"),
        ("Last synced on", "27/10/2026 10:30", "ro"))) +
    note("Visible to administrators only; agents see the Summary tab."),
    crumb="Accounts › Northwind Lettings (fictional) › Sync section",
    nav=["Home", "Recent", "#Customers", "*Accounts", "Contacts", "#Lettings", "Properties", "Leases"])

# --- Chapter 22 ---------------------------------------------------------
def _pack_cls(r, c):
    if r == 1: return "hdr"
    if c == 1 and r > 1: return "num"
    return ""

S["22/pack-xlsx"] = lambda: frame("Microsoft Excel — lease-pack-LSE-0042.xlsx", "excel", grid(
    ["A", "B", "C", "D"], [
        ["Invoice", "Amount {3}", "Due date", "Status"],
        ["INV-2026-0811", "1,250.00", "2026-09-01", "Paid"],
        ["INV-2026-0911", "1,250.00", "2026-10-01", "Paid"],
        ["INV-2026-1011", "1,250.00", "2026-11-01", "Open"],
    ], _pack_cls, ["Lease {1}", "Invoices {2}"], 1, ("B2", "1250")), top=False)

S["22/impersonation-role"] = lambda: frame("Power Platform admin center", "admin",
    h1("Harbourline Impersonation") +
    tabs("Core Records", "*Business Management", "Service Management", "Customization") +
    h2("Miscellaneous privileges") +
    table(["Privilege", "Level"], [
        ["Act on Behalf of Another User {1}", "Organization"],
        ["Go Mobile", "None"],
        ["Export to Excel", "None"]], selected=0) +
    note("Assign this role directly to the application user, not through a team {2}. Keep the role in its own name so audits can find it."),
    crumb="Security roles › Harbourline Impersonation › Business Management",
    nav=["Home", "#Manage", "*Environments", "#Settings", "Users + permissions", "Security roles"])

# --- Chapter 23 ---------------------------------------------------------
S["23/loadtest"] = lambda: frame("Azure portal", "azure",
    h1("property-search | Run 2026-10-27 14:00") +
    cards(("Requests per second {1}", "(measured)"), ("p95 response time {2}", "(measured)"),
          ("Errors {3}", "(measured)"), ("Virtual users", "50")) +
    two(panel("Client-side: response time", bars([30, 32, 31, 35, 48, 41, 36, 33])),
        panel("Server-side metrics {4}", table(["Metric", "Source"], [
            ["Function executions", "Application Insights"], ["Dataverse dependency duration", "Application Insights"],
            ["Instance count", "Function App metrics"]]))) +
    note("This book publishes no performance figures. Record your own results in the template from Chapter 23."),
    crumb="Azure Load Testing › Tests › property-search › a run",
    nav=["Overview", "#Tests", "*Tests", "Test runs", "#Settings", "Identity"])

# --- Chapter 24 ---------------------------------------------------------
S["24/dlq-peek"] = lambda: frame("Terminal — HarbourlineConnect", "terminal", term(
    "[p]~/HarbourlineConnect$[/p] dotnet run --project samples/Harbourline.Ops -- \\\n"
    "    dlq-peek sb-hlconnect-dev.servicebus.windows.net order-intake\n"
    "\n"
    "ORD-10081 {1}       Permanent {2}                2026-10-27 11:20:41Z  Property HBR-9999 does not exist. {3}\n"
    "ORD-10094         Permanent                2026-10-27 12:02:17Z  Empty body.\n"
    "2 message(s) shown."), top=False)

S["24/order-trace"] = lambda: frame("Azure portal", "azure",
    h1("ProcessOrder · order.number = ORD-10077") +
    gantt([("ProcessOrder (attempt 1)", 0, 22, True), ("  LedgerOne POST /orders: 503 {1}", 8, 10, True),
           ("ProcessOrder (attempt 2) {2}", 45, 40, False), ("  LedgerOne POST /orders: 200", 55, 15, False)]) +
    h2("Span attributes") +
    table(["Attribute", "Attempt 1", "Attempt 2"], [
        ["order.number", "ORD-10077", "ORD-10077"],
        ["messaging.servicebus.message.delivery_count {3}", "1", "2"],
        ["outcome", "Transient: abandoned", "Completed"]]),
    crumb="Application Insights › Transaction search › ProcessOrder (order.number = ORD-10077)",
    nav=["Overview", "#Investigate", "*Transaction search", "Failures", "Performance"])

# --- Chapter 25 ---------------------------------------------------------
S["25/advisor"] = lambda: frame("Azure portal", "azure",
    h1("Advisor | Recommendations") + cmds("Filter: resource group = rg-hlconnect-prod", "Download as CSV") +
    table(["Category", "Recommendation", "Impacted resources", "Impact"], [
        ["Reliability {1}", "Enable zone redundancy for the Function App", "1", "High"],
        ["Security {2}", "Storage account should disable shared key access", "1", "Medium"],
        ["Security", "Key Vault should have purge protection enabled", "1", "Medium"],
        ["Cost {3}", "Right-size or shut down under-used resources", "0", "Low"],
    ], selected=0) +
    note("Recommendations shown are typical examples; your list depends on your resources and configuration."),
    crumb="Advisor › Recommendations (filtered to rg-hlconnect-prod)",
    nav=["Overview", "#Recommendations", "*All recommendations", "Reliability", "Security", "Performance", "Cost"])

S["25/budget"] = lambda: frame("Azure portal", "azure",
    h1("hlconnect-prod") + cmds("Edit budget", "Delete", "Refresh") +
    fields(("Scope", "rg-hlconnect-prod", "ro"), ("Amount {1}", "£ 400 per month", "ro"),
           ("Reset period", "Monthly", "ro")) +
    h2("Alert conditions {2}") +
    table(["Type", "% of budget", "Amount"], [["Actual", "80%", "£ 320"], ["Forecasted", "100%", "£ 400"]]) +
    h2("Alert recipients {3}") +
    table(["Email"], [["platform-team@harbourline.example"], ["finance-ops@harbourline.example"]]),
    crumb="Cost Management › Budgets › hlconnect-prod",
    nav=["Overview", "#Cost Management", "Cost analysis", "Cost alerts", "*Budgets", "Advisor recommendations"])
