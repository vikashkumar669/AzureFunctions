using System.Globalization;
using Harbourline.Connect.Core.Dataverse;
using Harbourline.Connect.Core.Integrations;
using Harbourline.Connect.Core.Sync;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Harbourline.Connect.Core.Customers;

/// <summary>
/// Two-way customer sync between Dataverse accounts and LedgerOne
/// customers (Chapter 21). Accounts are matched on accountnumber,
/// which holds the LedgerOne customer id.
/// </summary>
public sealed partial class CustomerSyncService(
    IOrganizationServiceAsync2 dataverse,
    ChangeReader changes,
    ILedgerOneClient ledger,
    ICheckpointStore checkpoints,
    TimeProvider time,
    ILogger<CustomerSyncService> logger)
{
    public const string OutboundCheckpoint = "accounts-to-ledgerone";
    public const string InboundCheckpoint = "ledgerone-to-accounts";

    private static readonly string[] Columns =
    [
        AccountTable.Name, AccountTable.AccountNumber,
        AccountTable.Email, AccountTable.Phone, AccountTable.City,
        AccountTable.SyncHash,
    ];

    public static CustomerFields FieldsOf(Entity account)
    {
        ArgumentNullException.ThrowIfNull(account);
        return new CustomerFields(
            account.GetAttributeValue<string>(AccountTable.Name) ?? "",
            account.GetAttributeValue<string>(AccountTable.Email),
            account.GetAttributeValue<string>(AccountTable.Phone),
            account.GetAttributeValue<string>(AccountTable.City));
    }

    // <listing:sync-outbound>
    public async Task<int> PushAsync(CancellationToken ct)
    {
        var checkpoint = await checkpoints
            .GetAsync(OutboundCheckpoint, ct).ConfigureAwait(false);
        var set = await changes.ReadAsync(AccountTable.LogicalName,
            Columns, checkpoint.Token, ct).ConfigureAwait(false);
        var pushed = 0;

        foreach (var account in set.Upserted)
        {
            var id = account.GetAttributeValue<string>(
                AccountTable.AccountNumber);
            var fields = FieldsOf(account);
            var hash = fields.Hash();

            if (id is null || hash == account
                .GetAttributeValue<string>(AccountTable.SyncHash))
            {
                continue;                                  // <1>
            }

            await ledger.UpsertCustomerAsync(
                new LedgerCustomer(id, fields.Name, fields.Email,
                    fields.Phone, fields.City, time.GetUtcNow()),
                idempotencyKey: $"{id}-{hash}",            // <2>
                ct).ConfigureAwait(false);
            await StampAsync(account.Id, hash, "Dataverse", ct)
                .ConfigureAwait(false);                    // <3>
            pushed++;
        }

        await checkpoints.SaveAsync(new Checkpoint(
                OutboundCheckpoint, set.NewToken,
                time.GetUtcNow().UtcDateTime), ct)
            .ConfigureAwait(false);                        // <4>
        return pushed;
    }
    // </listing:sync-outbound>

    // <listing:sync-inbound>
    public async Task<int> PullAsync(CancellationToken ct)
    {
        var checkpoint = await checkpoints
            .GetAsync(InboundCheckpoint, ct).ConfigureAwait(false);
        var since = checkpoint.Token is null
            ? DateTimeOffset.UnixEpoch
            : DateTimeOffset.Parse(checkpoint.Token,
                CultureInfo.InvariantCulture);
        var startedAt = time.GetUtcNow();
        int applied = 0;

        for (int? page = 1; page is { } p;)
        {
            var result = await ledger.GetCustomersAsync(since, p, ct)
                .ConfigureAwait(false);
            foreach (var customer in result.Items)
            {
                if (await ApplyAsync(customer, ct)
                        .ConfigureAwait(false))
                {
                    applied++;
                }
            }

            page = result.NextPage;
        }

        await checkpoints.SaveAsync(new Checkpoint(
                InboundCheckpoint,
                startedAt.ToString("O", CultureInfo.InvariantCulture),
                startedAt.UtcDateTime), ct)
            .ConfigureAwait(false);
        return applied;
    }

    private async Task<bool> ApplyAsync(
        LedgerCustomer customer, CancellationToken ct)
    {
        var account = await FindAsync(customer.CustomerId, ct)
            .ConfigureAwait(false);
        if (account is null)
        {
            LogUnmatched(logger, customer.CustomerId);
            return false;                                  // <1>
        }

        var decision = ConflictResolver.Decide(
            FieldsOf(account),
            account.GetAttributeValue<string>(AccountTable.SyncHash),
            CustomerFields.From(customer));                // <2>
        var result = decision.Result;
        var hash = result.Hash();

        if (decision.Action is SyncAction.UpdateLedger
            or SyncAction.Merge)
        {
            await ledger.UpsertCustomerAsync(
                new LedgerCustomer(customer.CustomerId, result.Name,
                    result.Email, result.Phone, result.City,
                    time.GetUtcNow()),
                $"{customer.CustomerId}-{hash}", ct)
                .ConfigureAwait(false);
        }

        if (decision.Action is SyncAction.Skip
            && hash == account.GetAttributeValue<string>(
                AccountTable.SyncHash))
        {
            return false;
        }

        var update = new Entity(AccountTable.LogicalName, account.Id)
        {
            [AccountTable.Name] = result.Name,             // <3>
            [AccountTable.Email] = result.Email,
            [AccountTable.Phone] = result.Phone,
            [AccountTable.City] = result.City,
            [AccountTable.SyncHash] = hash,
            [AccountTable.LastSyncedFrom] = "LedgerOne",
        };
        await dataverse.UpdateAsync(update, ct).ConfigureAwait(false);
        LogDecision(logger, customer.CustomerId,
            decision.Action, decision.Reason);
        return true;
    }
    // </listing:sync-inbound>

    private async Task<Entity?> FindAsync(
        string customerId, CancellationToken ct)
    {
        var query = new QueryExpression(AccountTable.LogicalName)
        {
            ColumnSet = new ColumnSet(Columns),
            TopCount = 1,
        };
        query.Criteria.AddCondition(AccountTable.AccountNumber,
            ConditionOperator.Equal, customerId);
        var rows = await dataverse.RetrieveMultipleAsync(query, ct)
            .ConfigureAwait(false);
        return rows.Entities.FirstOrDefault();
    }

    private Task StampAsync(
        Guid accountId, string hash, string source,
        CancellationToken ct) =>
        dataverse.UpdateAsync(
            new Entity(AccountTable.LogicalName, accountId)
            {
                [AccountTable.SyncHash] = hash,
                [AccountTable.LastSyncedFrom] = source,
            }, ct);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "No account has LedgerOne id {CustomerId}")]
    private static partial void LogUnmatched(
        ILogger logger, string customerId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Customer {CustomerId}: {Action} ({Reason})")]
    private static partial void LogDecision(ILogger logger,
        string customerId, SyncAction action, string reason);
}
