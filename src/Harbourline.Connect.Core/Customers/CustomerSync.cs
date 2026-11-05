using System.Security.Cryptography;
using System.Text;
using Harbourline.Connect.Core.Integrations;

namespace Harbourline.Connect.Core.Customers;

/// <summary>The synchronised fields of a customer, from either side.</summary>
public sealed record CustomerFields(
    string Name, string? Email, string? Phone, string? City)
{
    // <listing:sync-hash>
    /// <summary>
    /// A short hash of the synchronised fields. Stored on both sides
    /// after each sync; an incoming change whose hash equals the stored
    /// one is an echo of our own write and is skipped.
    /// </summary>
    public string Hash()
    {
        var text = string.Join('\u001f',                   // <1>
            Name.Trim(),
            Email?.Trim().ToUpperInvariant(),
            Phone?.Trim(),
            City?.Trim());
        return Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(text)), 0, 8);
    }
    // </listing:sync-hash>

    public static CustomerFields From(LedgerCustomer c)
    {
        ArgumentNullException.ThrowIfNull(c);
        return new(c.Name, c.Email, c.Phone, c.City);
    }
}

public enum SyncAction
{
    Skip,
    UpdateDataverse,
    UpdateLedger,
    Merge,
}

public sealed record SyncDecision(
    SyncAction Action, CustomerFields Result, string Reason);

/// <summary>
/// Decides what to do when LedgerOne reports a changed customer
/// (Chapter 21). Pure logic, so every rule has a unit test.
/// </summary>
public static class ConflictResolver
{
    // <listing:conflict-rules>
    public static SyncDecision Decide(
        CustomerFields dataverse,
        string? lastSyncedHash,
        CustomerFields ledger)
    {
        ArgumentNullException.ThrowIfNull(dataverse);
        ArgumentNullException.ThrowIfNull(ledger);

        var dvHash = dataverse.Hash();
        var ledgerHash = ledger.Hash();

        if (dvHash == ledgerHash)                          // <1>
        {
            return new(SyncAction.Skip, dataverse, "Already equal.");
        }

        var dataverseChanged = dvHash != lastSyncedHash;   // <2>
        var ledgerChanged = ledgerHash != lastSyncedHash;

        if (ledgerChanged && !dataverseChanged)
        {
            return new(SyncAction.UpdateDataverse, ledger,
                "Only LedgerOne changed.");
        }

        if (dataverseChanged && !ledgerChanged)
        {
            return new(SyncAction.UpdateLedger, dataverse,
                "Only Dataverse changed.");
        }

        // Both changed since the last sync: merge by field owner.
        var merged = new CustomerFields(                   // <3>
            Name: dataverse.Name,      // Dataverse owns the name
            Email: ledger.Email,       // LedgerOne owns billing contact
            Phone: ledger.Phone,
            City: dataverse.City);     // Dataverse owns the address
        return new(SyncAction.Merge, merged,
            "Both changed; merged by field owner.");
    }
    // </listing:conflict-rules>
}
