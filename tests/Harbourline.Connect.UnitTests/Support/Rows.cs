using Harbourline.Connect.Core.Dataverse;
using Microsoft.Xrm.Sdk;

namespace Harbourline.Connect.UnitTests.Support;

public static class Rows
{
    public static Entity Property(
        string code, string name = "1 Quay Street", int bedrooms = 2,
        decimal rent = 950m, PropertyStatus status = PropertyStatus.Available)
    {
        return new Entity(PropertyTable.LogicalName, Guid.NewGuid())
        {
            [PropertyTable.Code] = code,
            [PropertyTable.Name] = name,
            [PropertyTable.City] = "Leeds",
            [PropertyTable.Postcode] = "LS1 4AP",
            [PropertyTable.Bedrooms] = bedrooms,
            [PropertyTable.MonthlyRent] = new Money(rent),
            [PropertyTable.Status] = new OptionSetValue((int)status),
            RowVersion = "1001",
        };
    }

    public static EntityCollection Collection(
        params Entity[] rows) => new(rows.ToList());
}
