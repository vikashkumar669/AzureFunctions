using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace Harbourline.Connect.Core.Sync;

/// <summary>Rows that changed since a token, plus the next token.</summary>
public sealed record ChangeSet(
    IReadOnlyList<Entity> Upserted,
    IReadOnlyList<EntityReference> Deleted,
    string NewToken);

/// <summary>
/// Reads Dataverse change tracking (RetrieveEntityChanges). The table
/// must have change tracking enabled. A null token returns every row
/// as new; deletions are returned only after the first token.
/// </summary>
public sealed class ChangeReader(IOrganizationServiceAsync2 dataverse)
{
    // <listing:change-reader>
    public async Task<ChangeSet> ReadAsync(
        string table,
        string[] columns,
        string? token,
        CancellationToken cancellationToken)
    {
        var upserted = new List<Entity>();
        var deleted = new List<EntityReference>();
        var request = new RetrieveEntityChangesRequest
        {
            EntityName = table,
            Columns = new ColumnSet(columns),
            DataVersion = token,                           // <1>
            PageInfo = new PagingInfo
            {
                Count = 5000, PageNumber = 1,
            },
        };

        while (true)
        {
            var response = (RetrieveEntityChangesResponse)
                await dataverse.ExecuteAsync(
                    request, cancellationToken)
                .ConfigureAwait(false);
            var changes = response.EntityChanges;

            foreach (var change in changes.Changes)
            {
                switch (change)                            // <2>
                {
                    case NewOrUpdatedItem item:
                        upserted.Add(item.NewOrUpdatedEntity);
                        break;
                    case RemovedOrDeletedItem removed:
                        deleted.Add(removed.RemovedItem);
                        break;
                }
            }

            if (!changes.MoreRecords)
            {
                return new ChangeSet(
                    upserted, deleted, changes.DataToken); // <3>
            }

            request.PageInfo.PageNumber++;
            request.PageInfo.PagingCookie = changes.PagingCookie;
        }
    }
    // </listing:change-reader>
}
