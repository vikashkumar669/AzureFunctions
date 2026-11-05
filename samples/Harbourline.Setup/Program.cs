using Azure.Identity;
using Harbourline.Connect.Core.Dataverse;
using Harbourline.Setup;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using static Harbourline.Setup.SchemaBuilder;

// Creates the Harbourline solution, tables, columns, keys and lookups
// in a DEVELOPER environment, signed in with your own account
// (az login). Never point it at production.

var url = args.SkipWhile(a => a != "--url").Skip(1).FirstOrDefault()
    ?? Environment.GetEnvironmentVariable("Dataverse__Url")
    ?? throw new InvalidOperationException(
        "Pass --url https://<org>.crm.dynamics.com");

using var client = DataverseClientFactory.Create(
    new DataverseOptions { Url = url },
    new DefaultAzureCredential(),
    NullLogger.Instance);

const string Solution = "HarbourlineConnect";

// <listing:setup-solution>
var publisher = await FindAsync("publisher", "uniquename", "harbourline")
    ?? await client.CreateAsync(new Entity("publisher")
    {
        ["uniquename"] = "harbourline",
        ["friendlyname"] = "Harbourline Group (fictional)",
        ["customizationprefix"] = "hl",                   // <1>
        ["customizationoptionvalueprefix"] = 10000,       // <2>
    });

if (await FindAsync("solution", "uniquename", Solution) is null)
{
    await client.CreateAsync(new Entity("solution")
    {
        ["uniquename"] = Solution,
        ["friendlyname"] = "Harbourline Connect",
        ["version"] = "1.0.0.0",
        ["publisherid"] = new EntityReference("publisher", publisher),
    });                                                    // <3>
}
// </listing:setup-solution>

var schema = new SchemaBuilder(client, Solution);
Console.WriteLine("Tables");
await schema.EnsureTableAsync("hl_Property", "Property", "Properties");
await schema.EnsureTableAsync("hl_Lease", "Lease", "Leases");
await schema.EnsureTableAsync("hl_Invoice", "Invoice", "Invoices");
await schema.EnsureTableAsync(
    "hl_ServiceOrder", "Service order", "Service orders");
await schema.EnsureTableAsync(
    "hl_MeterReading", "Meter reading", "Meter readings");
await schema.EnsureTableAsync("hl_SyncState", "Sync state", "Sync states");

Console.WriteLine("Columns");
// <listing:setup-columns>
await schema.EnsureColumnAsync("hl_property", TextColumn("hl_PropertyCode",
    "Property code", 10, AttributeRequiredLevel.ApplicationRequired));
await schema.EnsureColumnAsync("hl_property",
    TextColumn("hl_City", "City", 60));
await schema.EnsureColumnAsync("hl_property",
    TextColumn("hl_Postcode", "Postcode", 10));
await schema.EnsureColumnAsync("hl_property", new IntegerAttributeMetadata
{
    SchemaName = "hl_Bedrooms", DisplayName = L("Bedrooms"),
    MinValue = 0, MaxValue = 20, Format = IntegerFormat.None,
    RequiredLevel = None(),
});
await schema.EnsureColumnAsync("hl_property", MoneyColumn(
    "hl_MonthlyRent", "Monthly rent"));
await schema.EnsureColumnAsync("hl_property", Choice("hl_Status",
    "Status", "Available", "Let", "Maintenance"));
await schema.EnsureColumnAsync("hl_property", DateOnlyColumn(
    "hl_AvailableFrom", "Available from"));
// </listing:setup-columns>

await schema.EnsureColumnAsync("hl_lease", TextColumn("hl_LeaseNumber",
    "Lease number", 20, AttributeRequiredLevel.ApplicationRequired));
await schema.EnsureColumnAsync("hl_lease", DateOnlyColumn("hl_StartDate", "Start"));
await schema.EnsureColumnAsync("hl_lease", DateOnlyColumn("hl_EndDate", "End"));
await schema.EnsureColumnAsync("hl_lease", MoneyColumn("hl_MonthlyRent", "Rent"));
await schema.EnsureColumnAsync("hl_lease", Choice("hl_LeaseStatus",
    "Lease status", "Draft", "Awaiting signature", "Active", "Declined",
    "Ended"));
await schema.EnsureColumnAsync("hl_lease",
    TextColumn("hl_SignatureEnvelopeId", "Signature envelope", 64));

await schema.EnsureColumnAsync("hl_invoice", TextColumn("hl_InvoiceNumber",
    "Invoice number", 20, AttributeRequiredLevel.ApplicationRequired));
await schema.EnsureColumnAsync("hl_invoice", MoneyColumn("hl_Amount", "Amount"));
await schema.EnsureColumnAsync("hl_invoice", DateOnlyColumn("hl_DueDate", "Due"));
await schema.EnsureColumnAsync("hl_invoice", Choice("hl_InvoiceStatus",
    "Invoice status", "Open", "Paid", "Overdue", "Cancelled"));

await schema.EnsureColumnAsync("hl_serviceorder", TextColumn("hl_OrderNumber",
    "Order number", 20, AttributeRequiredLevel.ApplicationRequired));
await schema.EnsureColumnAsync("hl_serviceorder",
    TextColumn("hl_Category", "Category", 20));
await schema.EnsureColumnAsync("hl_serviceorder", MoneyColumn("hl_Amount", "Amount"));
await schema.EnsureColumnAsync("hl_serviceorder", Choice("hl_OrderStatus",
    "Order status", "Received", "Approved", "Rejected"));

await schema.EnsureColumnAsync("hl_meterreading",
    TextColumn("hl_ReadingKey", "Reading key", 100,
        AttributeRequiredLevel.ApplicationRequired));
await schema.EnsureColumnAsync("hl_meterreading", TextColumn("hl_Meter", "Meter", 20));
await schema.EnsureColumnAsync("hl_meterreading", new DateTimeAttributeMetadata
{
    SchemaName = "hl_ReadAt", DisplayName = L("Read at"),
    Format = DateTimeFormat.DateAndTime, RequiredLevel = None(),
    DateTimeBehavior = DateTimeBehavior.TimeZoneIndependent,
});
await schema.EnsureColumnAsync("hl_meterreading", new DecimalAttributeMetadata
{
    SchemaName = "hl_Value", DisplayName = L("Value"),
    Precision = 3, MinValue = 0, MaxValue = 100_000_000,
    RequiredLevel = None(),
});

await schema.EnsureColumnAsync("hl_syncstate", new MemoAttributeMetadata
{
    SchemaName = "hl_Token", DisplayName = L("Token"),
    MaxLength = 10_000, RequiredLevel = None(),
});
await schema.EnsureColumnAsync("hl_syncstate", new DateTimeAttributeMetadata
{
    SchemaName = "hl_LastRunUtc", DisplayName = L("Last run (UTC)"),
    Format = DateTimeFormat.DateAndTime, RequiredLevel = None(),
    DateTimeBehavior = DateTimeBehavior.UserLocal,
});

await schema.EnsureColumnAsync("account", TextColumn("hl_SyncHash", "Sync hash", 32));
await schema.EnsureColumnAsync("account",
    TextColumn("hl_LastSyncedFrom", "Last synced from", 20));

Console.WriteLine("Lookups");
await schema.EnsureLookupAsync("hl_lease", "hl_property", "hl_PropertyId", "Property");
await schema.EnsureLookupAsync("hl_lease", "contact", "hl_TenantId", "Tenant");
await schema.EnsureLookupAsync("hl_invoice", "hl_lease", "hl_LeaseId", "Lease");
await schema.EnsureLookupAsync(
    "hl_serviceorder", "hl_property", "hl_PropertyId", "Property");
await schema.EnsureLookupAsync(
    "hl_meterreading", "hl_property", "hl_PropertyId", "Property");

Console.WriteLine("Alternate keys");
await schema.EnsureKeyAsync("hl_property", "hl_propertycode_key", "hl_propertycode");
await schema.EnsureKeyAsync("hl_lease", "hl_leasenumber_key", "hl_leasenumber");
await schema.EnsureKeyAsync("hl_invoice", "hl_invoicenumber_key", "hl_invoicenumber");
await schema.EnsureKeyAsync(
    "hl_serviceorder", "hl_ordernumber_key", "hl_ordernumber");
await schema.EnsureKeyAsync(
    "hl_meterreading", "hl_readingkey_key", "hl_readingkey");
await schema.EnsureKeyAsync("hl_syncstate", "hl_name_key", "hl_name");
await schema.EnsureKeyAsync("account", "hl_accountnumber_key", "accountnumber");

Console.WriteLine("Done. Publish customizations in the maker portal.");

async Task<Guid?> FindAsync(string table, string column, string value)
{
    var query = new QueryExpression(table) { ColumnSet = new ColumnSet(false) };
    query.Criteria.AddCondition(column, ConditionOperator.Equal, value);
    var rows = await client.RetrieveMultipleAsync(query);
    return rows.Entities.FirstOrDefault()?.Id;
}

static AttributeRequiredLevelManagedProperty None() =>
    new(AttributeRequiredLevel.None);

static StringAttributeMetadata TextColumn(string schema, string display,
    int max, AttributeRequiredLevel level = AttributeRequiredLevel.None) =>
    new()
    {
        SchemaName = schema, DisplayName = L(display), MaxLength = max,
        RequiredLevel = new AttributeRequiredLevelManagedProperty(level),
    };

static MoneyAttributeMetadata MoneyColumn(string schema, string display) =>
    new()
    {
        SchemaName = schema, DisplayName = L(display),
        Precision = 2, PrecisionSource = 2, RequiredLevel = None(),
    };

static DateTimeAttributeMetadata DateOnlyColumn(string schema, string display) =>
    new()
    {
        SchemaName = schema, DisplayName = L(display),
        Format = DateTimeFormat.DateOnly,
        DateTimeBehavior = DateTimeBehavior.DateOnly,
        RequiredLevel = None(),
    };
