using System.Text;
using Harbourline.Connect.Core.Bulk;
using Harbourline.Connect.Core.Feeds;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.PowerPlatform.Dataverse.Client;
using NSubstitute;

namespace Harbourline.Connect.UnitTests.Feeds;

public sealed class PropertyFeedTests
{
    private static MemoryStream Text(string s) => new(Encoding.UTF8.GetBytes(s));

    private static async Task<List<FeedRow>> ReadAll(string name, string content)
    {
        var rows = new List<FeedRow>();
        await foreach (var row in PropertyFeedReader.ReadAsync(
            name, Text(content), TestContext.Current.CancellationToken))
        {
            rows.Add(row);
        }

        return rows;
    }

    [Fact]
    public async Task Csv_ReadsRows_AndReportsBadOnes()
    {
        var rows = await ReadAll("feed.csv", """
            code,name,city,postcode,bedrooms,monthlyRent,availableFrom
            HBR-0001,1 Quay Street,Leeds,LS1 4AP,2,950,2026-11-01
            HBR-0002,2 Quay Street,Leeds,LS1 4AP,two,980,2026-11-01
            """);

        Assert.Equal(2, rows.Count);
        Assert.Equal("HBR-0001", rows[0].Item!.Code);
        Assert.Equal(950m, rows[0].Item!.MonthlyRent);
        Assert.Null(rows[1].Item);
        Assert.NotNull(rows[1].ParseError);
    }

    [Fact]
    public async Task Json_StreamsArrayItems()
    {
        var rows = await ReadAll("feed.json", """
            [{"code":"HBR-0001","name":"A","bedrooms":1},
             {"code":"HBR-0002","name":"B","bedrooms":3}]
            """);
        Assert.Equal(new[] { "HBR-0001", "HBR-0002" },
            rows.Select(r => r.Item!.Code));
    }

    [Fact]
    public async Task Xml_ReadsAttributes()
    {
        var rows = await ReadAll("feed.xml", """
            <properties>
              <property code="HBR-0001" name="A" city="Leeds" postcode="LS1"
                        bedrooms="2" monthlyRent="950.50"
                        availableFrom="2026-11-01" />
            </properties>
            """);
        Assert.Equal(950.50m, Assert.Single(rows).Item!.MonthlyRent);
    }

    [Fact]
    public async Task UnknownExtension_IsNotSupported() =>
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            ReadAll("feed.txt", "x"));

    [Fact]
    public async Task Import_RejectsInvalidAndDuplicateRows()
    {
        var dataverse = Substitute.For<IOrganizationServiceAsync2>();
        var importer = new PropertyFeedImporter(
            new BulkWriter(dataverse, NullLogger<BulkWriter>.Instance));
        var rows = PropertyFeedReader.ReadCsvAsync(Text("""
            code,name,city,postcode,bedrooms,monthlyRent
            HBR-0001,1 Quay Street,Leeds,LS1 4AP,2,950
            HBR-0001,1 Quay Street,Leeds,LS1 4AP,2,950
            BAD,No code,Leeds,LS1 4AP,2,950
            """), TestContext.Current.CancellationToken);

        var result = await importer.ImportAsync(
            rows, TestContext.Current.CancellationToken);

        Assert.Equal(3, result.Read);
        Assert.Equal(1, result.Imported);
        Assert.Equal(2, result.Rejected.Count);
        Assert.Contains("Duplicate", result.Rejected[0].Reason,
            StringComparison.Ordinal);
        Assert.StartsWith("line,code,reason", result.ToCsvReport(),
            StringComparison.Ordinal);
    }
}
