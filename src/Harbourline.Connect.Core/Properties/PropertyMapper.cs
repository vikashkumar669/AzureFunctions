using Harbourline.Connect.Contracts.Properties;
using Harbourline.Connect.Core.Dataverse;
using Microsoft.Xrm.Sdk;

namespace Harbourline.Connect.Core.Properties;

/// <summary>Converts between Dataverse rows and API contracts.</summary>
public static class PropertyMapper
{
    // <listing:property-map>
    public static PropertyDto ToDto(Entity row)
    {
        ArgumentNullException.ThrowIfNull(row);

        var status = row.GetAttributeValue<OptionSetValue>(
            PropertyTable.Status);                         // <1>
        var available = row.GetAttributeValue<DateTime?>(
            PropertyTable.AvailableFrom);

        return new PropertyDto(
            row.GetAttributeValue<string>(PropertyTable.Code),
            row.GetAttributeValue<string>(PropertyTable.Name),
            row.GetAttributeValue<string>(PropertyTable.City),
            row.GetAttributeValue<string>(PropertyTable.Postcode),
            row.GetAttributeValue<int>(PropertyTable.Bedrooms),
            row.GetAttributeValue<Money>(
                PropertyTable.MonthlyRent)?.Value ?? 0m,   // <2>
            status is null
                ? "Unknown"
                : ((PropertyStatus)status.Value).ToString(), // <3>
            available is null
                ? null
                : DateOnly.FromDateTime(available.Value))  // <4>
        {
            Version = row.RowVersion,                      // <5>
        };
    }
    // </listing:property-map>

    public static Entity ToEntity(CreatePropertyRequest r)
    {
        ArgumentNullException.ThrowIfNull(r);

        var row = new Entity(PropertyTable.LogicalName)
        {
            [PropertyTable.Code] = r.Code,
            [PropertyTable.Name] = r.Name,
            [PropertyTable.City] = r.City,
            [PropertyTable.Postcode] = r.Postcode,
            [PropertyTable.Bedrooms] = r.Bedrooms,
            [PropertyTable.MonthlyRent] = new Money(r.MonthlyRent ?? 0m),
            [PropertyTable.Status] =
                new OptionSetValue((int)PropertyStatus.Available),
        };

        if (r.AvailableFrom is { } from)
        {
            row[PropertyTable.AvailableFrom] =
                from.ToDateTime(TimeOnly.MinValue);
        }

        return row;
    }

    // <listing:property-patch>
    public static Entity ToPatch(
        string code, UpdatePropertyRequest r, string? version)
    {
        ArgumentNullException.ThrowIfNull(r);

        var row = new Entity(
            PropertyTable.LogicalName,
            PropertyTable.Code, code)                      // <1>
        {
            RowVersion = version,                          // <2>
        };

        if (r.Name is not null)
        {
            row[PropertyTable.Name] = r.Name;              // <3>
        }

        if (r.Bedrooms is { } bedrooms)
        {
            row[PropertyTable.Bedrooms] = bedrooms;
        }

        if (r.MonthlyRent is { } rent)
        {
            row[PropertyTable.MonthlyRent] = new Money(rent);
        }

        if (r.Status is not null
            && PropertyValidator.TryParseStatus(r.Status, out var s))
        {
            row[PropertyTable.Status] = new OptionSetValue((int)s);
        }

        if (r.AvailableFrom is { } from)
        {
            row[PropertyTable.AvailableFrom] =
                from.ToDateTime(TimeOnly.MinValue);
        }

        return row;
    }
    // </listing:property-patch>
}
