using Harbourline.Connect.Core.Dataverse;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace Harbourline.Connect.Core.Sync;

/// <summary>A saved position: a watermark, a token or a batch number.</summary>
public sealed record Checkpoint(string Name, string? Token, DateTime? LastRunUtc);

public interface ICheckpointStore
{
    Task<Checkpoint> GetAsync(
        string name, CancellationToken cancellationToken);

    Task SaveAsync(
        Checkpoint checkpoint, CancellationToken cancellationToken);
}

/// <summary>
/// Stores checkpoints as rows of hl_syncstate, keyed by name. Keeping
/// them in Dataverse means they are backed up, auditable and visible
/// to administrators in a model-driven app.
/// </summary>
// <listing:checkpoint-store>
public sealed class DataverseCheckpointStore(
    IOrganizationServiceAsync2 dataverse) : ICheckpointStore
{
    public async Task<Checkpoint> GetAsync(
        string name, CancellationToken cancellationToken)
    {
        var query = new QueryExpression(SyncStateTable.LogicalName)
        {
            ColumnSet = new ColumnSet(
                SyncStateTable.Token, SyncStateTable.LastRunUtc),
            TopCount = 1,
        };
        query.Criteria.AddCondition(SyncStateTable.Name,
            ConditionOperator.Equal, name);

        var rows = await dataverse
            .RetrieveMultipleAsync(query, cancellationToken)
            .ConfigureAwait(false);
        var row = rows.Entities.FirstOrDefault();

        return new Checkpoint(                             // <1>
            name,
            row?.GetAttributeValue<string>(SyncStateTable.Token),
            row?.GetAttributeValue<DateTime?>(
                SyncStateTable.LastRunUtc));
    }

    public Task SaveAsync(
        Checkpoint checkpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        var row = new Entity(SyncStateTable.LogicalName,
            SyncStateTable.Name, checkpoint.Name)          // <2>
        {
            [SyncStateTable.Token] = checkpoint.Token,
            [SyncStateTable.LastRunUtc] = checkpoint.LastRunUtc,
        };

        return dataverse.ExecuteAsync(
            new UpsertRequest { Target = row },            // <3>
            cancellationToken);
    }
}
// </listing:checkpoint-store>
