using Harbourline.Connect.Core.Bulk;
using Harbourline.Connect.Core.Dataverse;
using Harbourline.Connect.Core.Migration;
using Harbourline.Connect.Core.Sync;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using NSubstitute;

namespace Harbourline.Connect.UnitTests.Migration;

public sealed class MigrationTests
{
    private static LegacyPropertyRow Legacy(string propRef, string state = "A") =>
        new(propRef, " 1 Quay Street ", "LEEDS", "ls1 4ap", "2", "1,250.00",
            state, "01/11/2026");

    // <listing:test-mapping>
    [Fact]
    public void Map_AppliesEveryRuleOfTheSpec()
    {
        var mapped = LegacyMapping.Map(Legacy("42"));
        var row = mapped.Row!;

        Assert.Null(mapped.Error);
        Assert.Equal("HBR-0042", row[PropertyTable.Code]);
        Assert.Equal("1 Quay Street", row[PropertyTable.Name]);
        Assert.Equal("Leeds", row[PropertyTable.City]);
        Assert.Equal("LS1 4AP", row[PropertyTable.Postcode]);
        Assert.Equal(1250m, ((Money)row[PropertyTable.MonthlyRent]).Value);
        Assert.Equal(new DateTime(2026, 11, 1),
            row[PropertyTable.AvailableFrom]);
    }
    // </listing:test-mapping>

    [Theory]
    [InlineData("A", PropertyStatus.Available)]
    [InlineData("L", PropertyStatus.Let)]
    [InlineData("V", PropertyStatus.Maintenance)]
    public void States_MapToStatus(string state, PropertyStatus expected) =>
        Assert.Equal(expected, LegacyMapping.StatusOf(state));

    [Fact]
    public void UnknownState_IsReportedNotGuessed()
    {
        var mapped = LegacyMapping.Map(Legacy("7", state: "X"));
        Assert.Null(mapped.Row);
        Assert.Contains("Unknown state", mapped.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Checksum_IgnoresOrder_ButNotContent()
    {
        var a = Reconciler.Fingerprint(LegacyMapping.Map(Legacy("1")).Row!);
        var b = Reconciler.Fingerprint(LegacyMapping.Map(Legacy("2")).Row!);

        Assert.Equal(Reconciler.Checksum([a, b]), Reconciler.Checksum([b, a]));
        Assert.NotEqual(Reconciler.Checksum([a, b]), Reconciler.Checksum([a]));
    }

    // <listing:test-resume>
    [Fact]
    public async Task Run_ResumesAfterTheLastCompletedBatch()
    {
        var checkpoints = Substitute.For<ICheckpointStore>();
        checkpoints.GetAsync("run-1", Arg.Any<CancellationToken>())
            .Returns(new Checkpoint("run-1", "1", null));  // batch 1 done
        var dataverse = Substitute.For<IOrganizationServiceAsync2>();
        var runner = new MigrationRunner(
            new BulkWriter(dataverse, NullLogger<BulkWriter>.Instance),
            checkpoints, TimeProvider.System,
            NullLogger<MigrationRunner>.Instance);
        var source = Enumerable.Range(1, MigrationRunner.BatchSize + 10)
            .Select(i => Legacy(i.ToString("0000"))).ToList();

        var report = await runner.RunAsync(
            "run-1", source, TestContext.Current.CancellationToken);

        Assert.Equal(1, report.SkippedBatches);
        Assert.Equal(10, report.Migrated);                 // batch 2 only
        await checkpoints.Received(1).SaveAsync(
            Arg.Is<Checkpoint>(c => c.Token == "2"),
            Arg.Any<CancellationToken>());
    }
    // </listing:test-resume>
}
