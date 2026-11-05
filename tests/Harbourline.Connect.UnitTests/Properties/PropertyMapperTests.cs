using Harbourline.Connect.Contracts.Properties;
using Harbourline.Connect.Core.Dataverse;
using Harbourline.Connect.Core.Properties;
using Harbourline.Connect.UnitTests.Support;
using Microsoft.Xrm.Sdk;

namespace Harbourline.Connect.UnitTests.Properties;

public sealed class PropertyMapperTests
{
    [Fact]
    public void ToDto_MapsChoiceMoneyAndVersion()
    {
        var dto = PropertyMapper.ToDto(
            Rows.Property("HBR-0007", rent: 1200m,
                status: PropertyStatus.Let));

        Assert.Equal("HBR-0007", dto.Code);
        Assert.Equal(1200m, dto.MonthlyRent);
        Assert.Equal("Let", dto.Status);
        Assert.Equal("1001", dto.Version);
    }

    [Fact]
    public void ToPatch_SetsOnlyChangedColumns_AndUsesKey()
    {
        var patch = PropertyMapper.ToPatch("HBR-0007",
            new UpdatePropertyRequest(null, 3, null, "Maintenance", null),
            version: "77");

        Assert.Equal("HBR-0007", patch.KeyAttributes[PropertyTable.Code]);
        Assert.Equal(2, patch.Attributes.Count);
        Assert.Equal(3, patch[PropertyTable.Bedrooms]);
        Assert.Equal((int)PropertyStatus.Maintenance,
            ((OptionSetValue)patch[PropertyTable.Status]).Value);
        Assert.Equal("77", patch.RowVersion);
    }
}
