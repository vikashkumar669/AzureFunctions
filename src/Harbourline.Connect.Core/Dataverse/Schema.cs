namespace Harbourline.Connect.Core.Dataverse;

// Logical names of the Harbourline tables and columns. The setup tool
// in samples/Harbourline.Setup creates them (Chapter 4); everything else
// refers to these constants instead of repeating string literals.

// <listing:schema-property>
public static class PropertyTable
{
    public const string LogicalName = "hl_property";
    public const string Id = "hl_propertyid";
    public const string Name = "hl_name";               // primary name
    public const string Code = "hl_propertycode";       // alternate key
    public const string City = "hl_city";
    public const string Postcode = "hl_postcode";
    public const string Bedrooms = "hl_bedrooms";       // whole number
    public const string MonthlyRent = "hl_monthlyrent"; // currency
    public const string Status = "hl_status";           // choice
    public const string AvailableFrom = "hl_availablefrom"; // date only
    public const string CodeKey = "hl_propertycode_key";
}

public enum PropertyStatus
{
    Available = 100_000_000,
    Let = 100_000_001,
    Maintenance = 100_000_002,
}
// </listing:schema-property>

public static class LeaseTable
{
    public const string LogicalName = "hl_lease";
    public const string Id = "hl_leaseid";
    public const string Name = "hl_name";
    public const string Number = "hl_leasenumber";      // alternate key
    public const string Property = "hl_propertyid";     // lookup
    public const string Tenant = "hl_tenantid";         // lookup: contact
    public const string StartDate = "hl_startdate";
    public const string EndDate = "hl_enddate";
    public const string MonthlyRent = "hl_monthlyrent";
    public const string Status = "hl_leasestatus";      // choice
    public const string EnvelopeId = "hl_signatureenvelopeid";
    public const string NumberKey = "hl_leasenumber_key";
}

public enum LeaseStatus
{
    Draft = 100_000_000,
    AwaitingSignature = 100_000_001,
    Active = 100_000_002,
    Declined = 100_000_003,
    Ended = 100_000_004,
}

public static class InvoiceTable
{
    public const string LogicalName = "hl_invoice";
    public const string Id = "hl_invoiceid";
    public const string Name = "hl_name";
    public const string Number = "hl_invoicenumber";    // alternate key
    public const string Lease = "hl_leaseid";           // lookup
    public const string Amount = "hl_amount";           // currency
    public const string DueDate = "hl_duedate";
    public const string Status = "hl_invoicestatus";    // choice
    public const string NumberKey = "hl_invoicenumber_key";
}

public enum InvoiceStatus
{
    Open = 100_000_000,
    Paid = 100_000_001,
    Overdue = 100_000_002,
    Cancelled = 100_000_003,
}

public static class OrderTable
{
    public const string LogicalName = "hl_serviceorder";
    public const string Id = "hl_serviceorderid";
    public const string Name = "hl_name";
    public const string Number = "hl_ordernumber";      // alternate key
    public const string Property = "hl_propertyid";     // lookup
    public const string Category = "hl_category";       // text
    public const string Amount = "hl_amount";           // currency
    public const string Status = "hl_orderstatus";      // choice
    public const string NumberKey = "hl_ordernumber_key";
}

public enum OrderStatus
{
    Received = 100_000_000,
    Approved = 100_000_001,
    Rejected = 100_000_002,
}

public static class MeterReadingTable
{
    public const string LogicalName = "hl_meterreading";
    public const string Name = "hl_name";
    public const string ReadingKey = "hl_readingkey";   // alternate key
    public const string Property = "hl_propertyid";     // lookup
    public const string Meter = "hl_meter";
    public const string ReadAt = "hl_readat";
    public const string Value = "hl_value";             // decimal
    public const string KeyName = "hl_readingkey_key";
}

/// <summary>Checkpoints for scheduled jobs and migrations.</summary>
public static class SyncStateTable
{
    public const string LogicalName = "hl_syncstate";
    public const string Id = "hl_syncstateid";
    public const string Name = "hl_name";               // alternate key
    public const string Token = "hl_token";             // multiline text
    public const string LastRunUtc = "hl_lastrunutc";
    public const string NameKey = "hl_name_key";
}

/// <summary>Standard Dataverse tables used by the customer sync.</summary>
public static class AccountTable
{
    public const string LogicalName = "account";
    public const string Id = "accountid";
    public const string Name = "name";
    public const string AccountNumber = "accountnumber"; // LedgerOne id
    public const string Email = "emailaddress1";
    public const string Phone = "telephone1";
    public const string City = "address1_city";
    public const string ModifiedOn = "modifiedon";
    public const string SyncHash = "hl_synchash";
    public const string LastSyncedFrom = "hl_lastsyncedfrom";
}
