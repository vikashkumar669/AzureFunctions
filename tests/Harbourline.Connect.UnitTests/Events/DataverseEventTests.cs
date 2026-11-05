using Harbourline.Connect.Core.Dataverse;
using Harbourline.Connect.Core.Events;

namespace Harbourline.Connect.UnitTests.Events;

public sealed class DataverseEventTests
{
    // A trimmed RemoteExecutionContext, shaped like the sample on the
    // Microsoft Learn page "Use webhooks", with fictional ids.
    private const string Json = """
        {
          "CorrelationId": "aaaa0000-bb11-2222-33cc-444444dddddd",
          "Depth": 1,
          "InitiatingUserId": "11bb11bb-cc22-dd33-ee44-55ff55ff55ff",
          "InputParameters": [{
            "key": "Target",
            "value": {
              "__type": "Entity:http://schemas.microsoft.com/xrm/2011/Contracts",
              "Attributes": [
                { "key": "hl_leasestatus",
                  "value": { "__type": "OptionSetValue:http://schemas.microsoft.com/xrm/2011/Contracts",
                             "Value": 100000001 } },
                { "key": "hl_leaseid", "value": "6d81597f-0f9f-e711-8122-000d3aa2331c" }
              ],
              "Id": "6d81597f-0f9f-e711-8122-000d3aa2331c",
              "LogicalName": "hl_lease"
            }
          }],
          "MessageName": "Update",
          "OperationId": "4af10637-4ea2-e711-8122-000d3aa2331c",
          "OrganizationId": "00aa00aa-bb11-cc22-dd33-44ee44ee44ee",
          "PostEntityImages": [{
            "key": "PostImage",
            "value": {
              "Attributes": [
                { "key": "hl_leasenumber", "value": "LSE-0042" },
                { "key": "hl_monthlyrent",
                  "value": { "__type": "Money:http://schemas.microsoft.com/xrm/2011/Contracts",
                             "Value": 1250.0000 } }
              ],
              "LogicalName": "hl_lease"
            }
          }],
          "PrimaryEntityId": "6d81597f-0f9f-e711-8122-000d3aa2331c",
          "PrimaryEntityName": "hl_lease"
        }
        """;

    [Fact]
    public void Parse_ReadsContextAndChangedChoice()
    {
        var evt = DataverseEvent.Parse(Json);

        Assert.Equal("Update", evt.MessageName);
        Assert.Equal("hl_lease", evt.PrimaryEntityName);
        Assert.Equal(1, evt.Depth);
        Assert.True(evt.Changed(LeaseTable.Status));
        Assert.Equal((int)LeaseStatus.AwaitingSignature,
            evt.GetChoice(LeaseTable.Status));
    }

    [Fact]
    public void Values_FallBackToPostImage()
    {
        var evt = DataverseEvent.Parse(Json);

        Assert.False(evt.Changed(LeaseTable.Number));
        Assert.Equal("LSE-0042", evt.GetString(LeaseTable.Number));
        Assert.Equal(1250m, evt.GetMoney(LeaseTable.MonthlyRent));
    }

    [Fact]
    public void TruncatedPayload_IsDetected()
    {
        var truncated = Json.Replace("\"InputParameters\"",
            "\"Removed\"", StringComparison.Ordinal);
        Assert.True(DataverseEvent.Parse(truncated).IsTruncated);
    }
}
