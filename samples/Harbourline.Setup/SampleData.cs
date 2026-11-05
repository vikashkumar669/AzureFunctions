using Harbourline.Connect.Core.Dataverse;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace Harbourline.Setup;

/// <summary>
/// Fictional sample data for the guided labs: three properties, one
/// tenant, four leases and one customer account. Every row is upserted
/// by its alternate key, so running the tool again changes nothing.
/// Run with --sample-data, in a DEVELOPER environment only.
/// </summary>
internal static class SampleData
{
    private const string TenantEmail = "alex.morgan@tenant.example";

    public static async Task SeedAsync(IOrganizationServiceAsync2 client)
    {
        ArgumentNullException.ThrowIfNull(client);

        (string Code, string Name, string Postcode, int Beds, decimal Rent)[] properties =
        [
            ("HBR-0042", "Harbour View Apartment 2", "LS1 4AP", 2, 1250m),
            ("HBR-0043", "Kirkgate Mews 14", "LS2 7DJ", 3, 985.50m),
            ("HBR-0051", "Wellington Court 7", "LS1 4DL", 2, 2100m),
        ];
        foreach (var p in properties)
        {
            await UpsertAsync(client, new Entity(
                PropertyTable.LogicalName, PropertyTable.Code, p.Code)
            {
                [PropertyTable.Code] = p.Code,
                [PropertyTable.Name] = p.Name,
                [PropertyTable.City] = "Leeds",
                [PropertyTable.Postcode] = p.Postcode,
                [PropertyTable.Bedrooms] = p.Beds,
                [PropertyTable.MonthlyRent] = new Money(p.Rent),
                [PropertyTable.Status] =
                    new OptionSetValue((int)PropertyStatus.Available),
                [PropertyTable.AvailableFrom] = new DateTime(2026, 11, 1),
            });
        }

        var tenant = await TenantAsync(client);

        // LSE-0001..0003 match the invoices served by the LedgerOne mock;
        // LSE-0042 is the lease used in the webhook and signing labs.
        (string Number, string Property)[] leases =
        [
            ("LSE-0001", "HBR-0042"),
            ("LSE-0002", "HBR-0043"),
            ("LSE-0003", "HBR-0051"),
            ("LSE-0042", "HBR-0042"),
        ];
        foreach (var l in leases)
        {
            await UpsertAsync(client, new Entity(
                LeaseTable.LogicalName, LeaseTable.Number, l.Number)
            {
                [LeaseTable.Number] = l.Number,
                [LeaseTable.Name] = l.Number,
                [LeaseTable.Property] = new EntityReference(
                    PropertyTable.LogicalName, PropertyTable.Code, l.Property),
                [LeaseTable.Tenant] = new EntityReference("contact", tenant),
                [LeaseTable.StartDate] = new DateTime(2026, 12, 1),
                [LeaseTable.Status] =
                    new OptionSetValue((int)LeaseStatus.Draft),
            });
        }

        await UpsertAsync(client, new Entity(
            AccountTable.LogicalName, AccountTable.AccountNumber, "C-1001")
        {
            [AccountTable.AccountNumber] = "C-1001",
            [AccountTable.Name] = "Northwind Lettings (fictional)",
            [AccountTable.City] = "Leeds",
            [AccountTable.Email] = "accounts@northwind-lettings.example",
            [AccountTable.Phone] = "0113 496 0000",
        });

        Console.WriteLine("Sample data: 3 properties, 1 tenant, "
            + "4 leases, 1 account.");
    }

    private static Task UpsertAsync(
        IOrganizationServiceAsync2 client, Entity row) =>
        client.ExecuteAsync(new UpsertRequest { Target = row });

    private static async Task<Guid> TenantAsync(
        IOrganizationServiceAsync2 client)
    {
        var query = new QueryExpression("contact")
        {
            ColumnSet = new ColumnSet(false),
        };
        query.Criteria.AddCondition(
            "emailaddress1", ConditionOperator.Equal, TenantEmail);
        var found = await client.RetrieveMultipleAsync(query);
        if (found.Entities.Count > 0)
        {
            return found.Entities[0].Id;
        }

        return await client.CreateAsync(new Entity("contact")
        {
            ["firstname"] = "Alex",
            ["lastname"] = "Morgan (fictional)",
            ["emailaddress1"] = TenantEmail,
        });
    }
}
