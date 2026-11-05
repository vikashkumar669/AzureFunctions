using Harbourline.Connect.Core.Dataverse;
using Harbourline.Connect.Core.Search;
using Harbourline.Connect.UnitTests.Support;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk.Query;
using NSubstitute;

namespace Harbourline.Connect.UnitTests.Search;

public sealed class PropertySearchTests
{
    [Fact]
    public void Cursor_RoundTrips()
    {
        var cursor = new SearchCursor("1 Quay Street", Guid.NewGuid());
        Assert.Equal(cursor, PropertySearch.DecodeCursor(
            PropertySearch.EncodeCursor(cursor)));
    }

    // <listing:test-keyset>
    [Fact]
    public async Task NextPage_SeeksPastTheLastRow()
    {
        var dataverse = Substitute.For<IOrganizationServiceAsync2>();
        dataverse.RetrieveMultipleAsync(Arg.Any<QueryBase>(),
                Arg.Any<CancellationToken>())
            .Returns(Rows.Collection(Rows.Property("HBR-0001")));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var search = new PropertySearch(dataverse, cache);
        var after = PropertySearch.EncodeCursor(
            new SearchCursor("1 Quay Street", Guid.Empty));

        await search.SearchAsync("Leeds", null, after, 20,
            TestContext.Current.CancellationToken);

        await dataverse.Received(1).RetrieveMultipleAsync(
            Arg.Is<QueryExpression>(q =>
                q.TopCount == 21                           // <1>
                && q.Criteria.Filters.Single().FilterOperator
                    == LogicalOperator.Or),                // <2>
            Arg.Any<CancellationToken>());
    }
    // </listing:test-keyset>

    [Fact]
    public async Task FirstPage_IsServedFromCache()
    {
        var dataverse = Substitute.For<IOrganizationServiceAsync2>();
        dataverse.RetrieveMultipleAsync(Arg.Any<QueryBase>(),
                Arg.Any<CancellationToken>())
            .Returns(Rows.Collection(Rows.Property("HBR-0001")));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var search = new PropertySearch(dataverse, cache);
        var ct = TestContext.Current.CancellationToken;

        await search.SearchAsync("Leeds", 2, null, 20, ct);
        await search.SearchAsync("leeds", 2, null, 20, ct);

        await dataverse.Received(1).RetrieveMultipleAsync(
            Arg.Any<QueryBase>(), Arg.Any<CancellationToken>());
    }
}
