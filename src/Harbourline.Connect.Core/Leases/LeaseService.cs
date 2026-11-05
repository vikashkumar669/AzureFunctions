using Harbourline.Connect.Core.Dataverse;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Harbourline.Connect.Core.Leases;

/// <summary>What the signing workflow needs to know about a lease.</summary>
public sealed record LeaseSigningDetails(
    string LeaseNumber,
    string TenantName,
    string TenantEmail,
    string PropertyName);

/// <summary>Lease reads and status changes (Chapters 9 and 12).</summary>
public sealed class LeaseService(IOrganizationServiceAsync2 dataverse)
{
    // <listing:lease-details>
    public async Task<LeaseSigningDetails?> GetSigningDetailsAsync(
        string leaseNumber, CancellationToken cancellationToken)
    {
        var query = new QueryExpression(LeaseTable.LogicalName)
        {
            ColumnSet = new ColumnSet(LeaseTable.Number),
            TopCount = 1,
        };
        query.Criteria.AddCondition(LeaseTable.Number,
            ConditionOperator.Equal, leaseNumber);

        var tenant = query.AddLink("contact",              // <1>
            LeaseTable.Tenant, "contactid");
        tenant.EntityAlias = "tenant";
        tenant.Columns = new ColumnSet("fullname", "emailaddress1");

        var property = query.AddLink(PropertyTable.LogicalName,
            LeaseTable.Property, PropertyTable.Id);
        property.EntityAlias = "property";
        property.Columns = new ColumnSet(PropertyTable.Name);

        var rows = await dataverse
            .RetrieveMultipleAsync(query, cancellationToken)
            .ConfigureAwait(false);
        var row = rows.Entities.FirstOrDefault();
        if (row is null)
        {
            return null;
        }

        return new LeaseSigningDetails(
            leaseNumber,
            Aliased(row, "tenant.fullname"),               // <2>
            Aliased(row, "tenant.emailaddress1"),
            Aliased(row, "property." + PropertyTable.Name));
    }

    private static string Aliased(Entity row, string name) =>
        row.GetAttributeValue<AliasedValue>(name)?.Value as string
            ?? "";
    // </listing:lease-details>

    // <listing:lease-status>
    public Task SetStatusAsync(
        string leaseNumber,
        LeaseStatus status,
        string? envelopeId,
        CancellationToken cancellationToken)
    {
        var lease = new Entity(
            LeaseTable.LogicalName, LeaseTable.Number, leaseNumber)
        {
            [LeaseTable.Status] = new OptionSetValue((int)status),
        };

        if (envelopeId is not null)
        {
            lease[LeaseTable.EnvelopeId] = envelopeId;
        }

        return dataverse.UpdateAsync(lease, cancellationToken);
    }
    // </listing:lease-status>
}
