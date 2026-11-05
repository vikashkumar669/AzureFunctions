using Harbourline.Connect.Core.Customers;

namespace Harbourline.Connect.UnitTests.Customers;

public sealed class ConflictResolverTests
{
    private static readonly CustomerFields Synced =
        new("Northwind Lettings", "accounts@northwind.example", "0113 000 0000", "Leeds");

    [Fact]
    public void EqualSides_AreSkipped() =>
        Assert.Equal(SyncAction.Skip,
            ConflictResolver.Decide(Synced, Synced.Hash(), Synced).Action);

    [Fact]
    public void OnlyLedgerChanged_UpdatesDataverse()
    {
        var ledger = Synced with { Phone = "0113 999 9999" };
        var decision = ConflictResolver.Decide(Synced, Synced.Hash(), ledger);

        Assert.Equal(SyncAction.UpdateDataverse, decision.Action);
        Assert.Equal("0113 999 9999", decision.Result.Phone);
    }

    [Fact]
    public void OnlyDataverseChanged_UpdatesLedger()
    {
        var dataverse = Synced with { City = "York" };
        Assert.Equal(SyncAction.UpdateLedger,
            ConflictResolver.Decide(dataverse, Synced.Hash(), Synced).Action);
    }

    // <listing:test-merge>
    [Fact]
    public void BothChanged_MergesByFieldOwner()
    {
        var dataverse = Synced with { Name = "Northwind Homes", City = "York" };
        var ledger = Synced with { Email = "billing@northwind.example",
                                   City = "Hull" };

        var decision = ConflictResolver.Decide(
            dataverse, Synced.Hash(), ledger);

        Assert.Equal(SyncAction.Merge, decision.Action);
        Assert.Equal("Northwind Homes", decision.Result.Name);  // Dataverse
        Assert.Equal("York", decision.Result.City);             // Dataverse
        Assert.Equal("billing@northwind.example",
            decision.Result.Email);                             // LedgerOne
    }
    // </listing:test-merge>

    [Fact]
    public void Hash_IgnoresEmailCaseAndSpaces() =>
        Assert.Equal(Synced.Hash(),
            (Synced with { Email = " ACCOUNTS@northwind.example " }).Hash());
}
