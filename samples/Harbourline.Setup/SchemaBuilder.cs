using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;

namespace Harbourline.Setup;

/// <summary>
/// Creates tables, columns, keys and lookups through the Dataverse
/// metadata API. Every step checks first, so running it twice is safe.
/// </summary>
public sealed class SchemaBuilder(
    IOrganizationServiceAsync2 service, string solution)
{
    private const int English = 1033;

    public static Label L(string text) => new(text, English);

    // <listing:setup-table>
    public async Task EnsureTableAsync(
        string schemaName, string display, string plural,
        bool changeTracking = false)
    {
        var logicalName = schemaName.ToLowerInvariant();
        if (await ExistsAsync(logicalName))
        {
            Console.WriteLine($"  table {logicalName}: exists");
            return;                                        // <1>
        }

        await service.ExecuteAsync(new CreateEntityRequest
        {
            Entity = new EntityMetadata
            {
                SchemaName = schemaName,
                DisplayName = L(display),
                DisplayCollectionName = L(plural),
                OwnershipType = OwnershipTypes.UserOwned,  // <2>
                IsActivity = false,
                ChangeTrackingEnabled = changeTracking,    // <3>
            },
            PrimaryAttribute = new StringAttributeMetadata
            {
                SchemaName = "hl_Name",
                DisplayName = L("Name"),
                MaxLength = 100,
                RequiredLevel = new AttributeRequiredLevelManagedProperty(
                    AttributeRequiredLevel.ApplicationRequired),
            },
            SolutionUniqueName = solution,                 // <4>
        });
        Console.WriteLine($"  table {logicalName}: created");
    }
    // </listing:setup-table>

    public async Task EnsureColumnAsync(
        string table, AttributeMetadata column)
    {
        ArgumentNullException.ThrowIfNull(column);
        var existing = (RetrieveEntityResponse)await service
            .ExecuteAsync(new RetrieveEntityRequest
            {
                LogicalName = table,
                EntityFilters = EntityFilters.Attributes,
            });
        var name = column.SchemaName.ToLowerInvariant();
        if (existing.EntityMetadata.Attributes
            .Any(a => a.LogicalName == name))
        {
            return;
        }

        await service.ExecuteAsync(new CreateAttributeRequest
        {
            EntityName = table,
            Attribute = column,
            SolutionUniqueName = solution,
        });
        Console.WriteLine($"  column {table}.{name}: created");
    }

    // <listing:setup-key>
    public async Task EnsureKeyAsync(
        string table, string keySchemaName, string column)
    {
        var existing = (RetrieveEntityResponse)await service
            .ExecuteAsync(new RetrieveEntityRequest
            {
                LogicalName = table,
                EntityFilters = EntityFilters.Entity,
            });
        var keys = existing.EntityMetadata.Keys ?? [];
        if (keys.Any(k => k.SchemaName == keySchemaName))
        {
            return;
        }

        await service.ExecuteAsync(new CreateEntityKeyRequest
        {
            EntityName = table,
            EntityKey = new EntityKeyMetadata
            {
                SchemaName = keySchemaName,
                DisplayName = L(keySchemaName),
                KeyAttributes = [column],                  // <1>
            },
            SolutionUniqueName = solution,
        });
        Console.WriteLine($"  key {keySchemaName}: created " +
            "(index builds in the background)");           // <2>
    }
    // </listing:setup-key>

    public async Task EnsureLookupAsync(
        string from, string to, string lookupSchemaName, string display)
    {
        var existing = (RetrieveEntityResponse)await service
            .ExecuteAsync(new RetrieveEntityRequest
            {
                LogicalName = from,
                EntityFilters = EntityFilters.Attributes,
            });
        if (existing.EntityMetadata.Attributes.Any(a =>
                a.LogicalName == lookupSchemaName.ToLowerInvariant()))
        {
            return;
        }

        await service.ExecuteAsync(new CreateOneToManyRequest
        {
            OneToManyRelationship = new OneToManyRelationshipMetadata
            {
                ReferencedEntity = to,
                ReferencingEntity = from,
                SchemaName = $"hl_{to}_{from}_{lookupSchemaName}",
                CascadeConfiguration = new CascadeConfiguration
                {
                    Assign = CascadeType.NoCascade,
                    Delete = CascadeType.Restrict,
                    Merge = CascadeType.NoCascade,
                    Reparent = CascadeType.NoCascade,
                    Share = CascadeType.NoCascade,
                    Unshare = CascadeType.NoCascade,
                },
            },
            Lookup = new LookupAttributeMetadata
            {
                SchemaName = lookupSchemaName,
                DisplayName = L(display),
                RequiredLevel = new AttributeRequiredLevelManagedProperty(
                    AttributeRequiredLevel.None),
            },
            SolutionUniqueName = solution,
        });
        Console.WriteLine($"  lookup {from}.{lookupSchemaName}: created");
    }

    public static PicklistAttributeMetadata Choice(
        string schemaName, string display, params string[] labels)
    {
        ArgumentNullException.ThrowIfNull(labels);
        var options = new OptionSetMetadata
        {
            IsGlobal = false,
            OptionSetType = OptionSetType.Picklist,
        };

        // Values follow the publisher's option prefix (10000):
        // 100000000, 100000001 … match the enums in Schema.cs.
        for (var i = 0; i < labels.Length; i++)
        {
            options.Options.Add(
                new OptionMetadata(L(labels[i]), 100_000_000 + i));
        }

        return new PicklistAttributeMetadata
        {
            SchemaName = schemaName,
            DisplayName = L(display),
            RequiredLevel = new AttributeRequiredLevelManagedProperty(
                AttributeRequiredLevel.None),
            OptionSet = options,
        };
    }

    private async Task<bool> ExistsAsync(string logicalName)
    {
        try
        {
            await service.ExecuteAsync(new RetrieveEntityRequest
            {
                LogicalName = logicalName,
                EntityFilters = EntityFilters.Entity,
            });
            return true;
        }
        catch (System.ServiceModel.FaultException<
            OrganizationServiceFault>)
        {
            return false;
        }
    }
}
