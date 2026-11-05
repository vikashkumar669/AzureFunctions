using Harbourline.Connect.Core.Bulk;
using Harbourline.Connect.Core.Dataverse;
using Harbourline.Connect.UnitTests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using NSubstitute;

namespace Harbourline.Connect.UnitTests.Bulk;

public sealed class BulkWriterTests
{
    private static List<Entity> MakeRows(int count) =>
        Enumerable.Range(1, count)
            .Select(i => Rows.Property($"HBR-{i:0000}"))
            .ToList();

    private static string Key(Entity e) =>
        e.GetAttributeValue<string>(PropertyTable.Code);

    [Fact]
    public async Task Rows_AreSentInBatchesOfTheConfiguredSize()
    {
        var dataverse = Substitute.For<IOrganizationServiceAsync2>();
        var writer = new BulkWriter(dataverse, NullLogger<BulkWriter>.Instance);

        var result = await writer.UpsertAsync(PropertyTable.LogicalName,
            MakeRows(450), Key, new BulkOptions(BatchSize: 200, Parallelism: 2),
            TestContext.Current.CancellationToken);

        Assert.Equal(450, result.Succeeded);
        Assert.Equal(3, result.Requests);                  // 200 + 200 + 50
        await dataverse.Received(3).ExecuteAsync(
            Arg.Is<UpsertMultipleRequest>(r =>
                r.Targets.EntityName == PropertyTable.LogicalName),
            Arg.Any<CancellationToken>());
    }

    // <listing:test-split>
    [Fact]
    public async Task FailedBatch_IsSplitUntilTheBadRowIsFound()
    {
        // Any request that contains HBR-0006 fails, like a real batch
        // on a standard table: all or nothing.
        var dataverse = Substitute.For<IOrganizationServiceAsync2>();
        dataverse.ExecuteAsync(Arg.Any<UpsertMultipleRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<UpsertMultipleRequest>();
                return request.Targets.Entities.Any(
                        e => Key(e) == "HBR-0006")
                    ? throw Faults.Of(DataverseErrors.InvalidArgument,
                        "Postcode is not valid.")
                    : Task.FromResult<OrganizationResponse>(
                        new UpsertMultipleResponse());
            });
        var writer = new BulkWriter(dataverse, NullLogger<BulkWriter>.Instance);

        var result = await writer.UpsertAsync(PropertyTable.LogicalName,
            MakeRows(8), Key, new BulkOptions(BatchSize: 8, Parallelism: 1),
            TestContext.Current.CancellationToken);

        Assert.Equal(7, result.Succeeded);
        var error = Assert.Single(result.Errors);
        Assert.Equal("HBR-0006", error.Key);
        Assert.Equal("Postcode is not valid.", error.Message);
    }
    // </listing:test-split>

    [Fact]
    public void Options_AreValidated() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new BulkOptions(BatchSize: 5000).Validate());
}
