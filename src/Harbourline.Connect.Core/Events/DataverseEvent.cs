using System.Globalization;
using System.Text.Json;

namespace Harbourline.Connect.Core.Events;

/// <summary>
/// The parts of a Dataverse RemoteExecutionContext that integrations
/// need. Webhooks and Service Bus endpoints (JSON format) send the same
/// context, so one reader serves both (Chapters 9 and 11).
/// </summary>
public sealed class DataverseEvent
{
    private readonly Dictionary<string, JsonElement> target;
    private readonly Dictionary<string, JsonElement> postImage;

    private DataverseEvent(
        JsonElement root,
        Dictionary<string, JsonElement> target,
        Dictionary<string, JsonElement> postImage)
    {
        MessageName = root.GetProperty("MessageName").GetString() ?? "";
        PrimaryEntityName =
            root.GetProperty("PrimaryEntityName").GetString() ?? "";
        PrimaryEntityId = root.GetProperty("PrimaryEntityId").GetGuid();
        OrganizationId = root.GetProperty("OrganizationId").GetGuid();
        CorrelationId = root.GetProperty("CorrelationId").GetGuid();
        OperationId = root.GetProperty("OperationId").GetGuid();
        InitiatingUserId =
            root.GetProperty("InitiatingUserId").GetGuid();
        Depth = root.GetProperty("Depth").GetInt32();
        this.target = target;
        this.postImage = postImage;
    }

    public string MessageName { get; }
    public string PrimaryEntityName { get; }
    public Guid PrimaryEntityId { get; }
    public Guid OrganizationId { get; }
    public Guid CorrelationId { get; }
    public Guid OperationId { get; }
    public Guid InitiatingUserId { get; }
    public int Depth { get; }

    /// <summary>
    /// True when Dataverse removed InputParameters and images because
    /// the payload was too large: read the row from Dataverse instead.
    /// </summary>
    public bool IsTruncated => target.Count == 0;

    // <listing:event-parse>
    public static DataverseEvent Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement.Clone();           // <1>

        return new DataverseEvent(
            root,
            AttributesOf(root, "InputParameters", "Target"),
            AttributesOf(root, "PostEntityImages", null));
    }

    private static Dictionary<string, JsonElement> AttributesOf(
        JsonElement root, string collection, string? key)
    {
        var result = new Dictionary<string, JsonElement>(
            StringComparer.OrdinalIgnoreCase);

        if (!root.TryGetProperty(collection, out var items)
            || items.ValueKind != JsonValueKind.Array)
        {
            return result;                                 // <2>
        }

        foreach (var item in items.EnumerateArray())       // <3>
        {
            if (key is not null
                && item.GetProperty("key").GetString() != key)
            {
                continue;
            }

            var entity = item.GetProperty("value");
            if (entity.ValueKind != JsonValueKind.Object
                || !entity.TryGetProperty("Attributes", out var a))
            {
                continue;
            }

            foreach (var pair in a.EnumerateArray())
            {
                result[pair.GetProperty("key").GetString()!] =
                    pair.GetProperty("value");
            }

            break;                                         // <4>
        }

        return result;
    }
    // </listing:event-parse>

    /// <summary>Was this column part of the change?</summary>
    public bool Changed(string column) => target.ContainsKey(column);

    // <listing:event-values>
    public string? GetString(string column) =>
        Find(column) is { ValueKind: JsonValueKind.String } v
            ? v.GetString()
            : null;

    public int? GetChoice(string column) =>
        Find(column) is { ValueKind: JsonValueKind.Object } v
            && v.TryGetProperty("Value", out var value)    // <1>
                ? value.GetInt32()
                : null;

    public decimal? GetMoney(string column) =>
        Find(column) is { ValueKind: JsonValueKind.Object } v
            && v.TryGetProperty("Value", out var value)
                ? value.GetDecimal()
                : null;

    public Guid? GetLookupId(string column) =>
        Find(column) is { ValueKind: JsonValueKind.Object } v
            && v.TryGetProperty("Id", out var id)
                ? id.GetGuid()
                : null;

    private JsonElement? Find(string column) =>
        target.TryGetValue(column, out var v) ? v
        : postImage.TryGetValue(column, out var p) ? p   // <2>
        : null;
    // </listing:event-values>

    public override string ToString() => string.Format(
        CultureInfo.InvariantCulture, "{0} {1} {2}",
        MessageName, PrimaryEntityName, PrimaryEntityId);
}
