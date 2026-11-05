using Harbourline.Connect.Core.Dataverse;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Harbourline.Connect.Core.Orders;

public sealed record OrderStatusView(string OrderNumber, string Status);

/// <summary>Answers "what happened to my order?" for partners.</summary>
public sealed class OrderStatusReader(IOrganizationServiceAsync2 dataverse)
{
    public async Task<OrderStatusView?> GetAsync(
        string orderNumber, CancellationToken ct)
    {
        var query = new QueryExpression(OrderTable.LogicalName)
        {
            ColumnSet = new ColumnSet(OrderTable.Status),
            TopCount = 1,
        };
        query.Criteria.AddCondition(OrderTable.Number,
            ConditionOperator.Equal, orderNumber);
        var row = (await dataverse.RetrieveMultipleAsync(query, ct)
            .ConfigureAwait(false)).Entities.FirstOrDefault();

        return row is null
            ? null
            : new OrderStatusView(orderNumber,
                ((OrderStatus)row.GetAttributeValue<OptionSetValue>(
                    OrderTable.Status).Value).ToString());
    }
}
