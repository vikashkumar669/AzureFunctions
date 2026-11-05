using Harbourline.Connect.Contracts.Properties;
using Harbourline.Connect.Core.Common;
using Harbourline.Connect.Core.Dataverse;
using Harbourline.Connect.Core.Properties;
using Harbourline.Connect.UnitTests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Harbourline.Connect.UnitTests.Properties;

public sealed class PropertyServiceTests
{
    private readonly IOrganizationServiceAsync2 dataverse =
        Substitute.For<IOrganizationServiceAsync2>();

    private PropertyService Service() =>
        new(dataverse, NullLogger<PropertyService>.Instance);

    // <listing:test-list>
    [Fact]
    public async Task List_ReturnsContinuationToken_WhenMoreRecords()
    {
        // Arrange: Dataverse returns one row and says there are more.
        var page = Rows.Collection(Rows.Property("HBR-0001"));
        page.MoreRecords = true;
        page.PagingCookie = "<cookie/>";
        dataverse.RetrieveMultipleAsync(
                Arg.Any<QueryBase>(), Arg.Any<CancellationToken>())
            .Returns(page);                                // <1>

        // Act
        var result = await Service().ListAsync(
            new PropertyQuery("Leeds", "Available", 2, PageSize: 1),
            null, TestContext.Current.CancellationToken);

        // Assert: the query Dataverse received, and the result.
        await dataverse.Received(1).RetrieveMultipleAsync(
            Arg.Is<QueryExpression>(q =>
                q.PageInfo.Count == 1
                && q.Criteria.Conditions.Count == 3),      // <2>
            Arg.Any<CancellationToken>());
        Assert.Single(result.Items);
        Assert.NotNull(result.ContinuationToken);
    }
    // </listing:test-list>

    [Fact]
    public async Task Get_ReturnsNull_WhenKeyNotFound()
    {
        dataverse.ExecuteAsync(Arg.Any<RetrieveRequest>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(Faults.Of(
                DataverseErrors.RecordNotFoundByEntityKey));

        var result = await Service().GetByCodeAsync(
            "HBR-9999", TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    // <listing:test-concurrency>
    [Fact]
    public async Task Update_MapsVersionMismatch_ToPreconditionFailed()
    {
        dataverse.ExecuteAsync(Arg.Any<UpdateRequest>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(Faults.Of(
                DataverseErrors.ConcurrencyVersionMismatch)); // <1>

        var outcome = await Service().UpdateAsync("HBR-0001",
            new UpdatePropertyRequest(null, 3, null, null, null),
            ifMatch: "1001", TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeStatus.PreconditionFailed, outcome.Status);
        await dataverse.Received(1).ExecuteAsync(
            Arg.Is<UpdateRequest>(r =>
                r.ConcurrencyBehavior
                    == ConcurrencyBehavior.IfRowVersionMatches
                && r.Target.RowVersion == "1001"),         // <2>
            Arg.Any<CancellationToken>());
    }
    // </listing:test-concurrency>

    [Fact]
    public async Task Create_MapsDuplicateKey_ToConflict()
    {
        dataverse.CreateAsync(Arg.Any<Entity>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(Faults.Of(
                DataverseErrors.DuplicateRecordEntityKey));

        var outcome = await Service().CreateAsync(
            new CreatePropertyRequest("HBR-0001", "1 Quay Street",
                "Leeds", "LS1 4AP", 2, 950m, null),
            TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeStatus.Conflict, outcome.Status);
    }

    [Fact]
    public async Task Create_InvalidRequest_NeverCallsDataverse()
    {
        var outcome = await Service().CreateAsync(
            new CreatePropertyRequest(null, null, null, null, null,
                null, null),
            TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeStatus.Invalid, outcome.Status);
        Assert.Empty(dataverse.ReceivedCalls());
    }
}
