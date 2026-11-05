using System.Globalization;
using System.Text;
using Harbourline.Connect.Core.Bulk;
using Harbourline.Connect.Core.Dataverse;
using Harbourline.Connect.Core.Properties;
using Microsoft.Xrm.Sdk;

namespace Harbourline.Connect.Core.Feeds;

public sealed record FeedRejection(int Line, string? Code, string Reason);

public sealed record FeedImportResult(
    int Read,
    int Imported,
    IReadOnlyList<FeedRejection> Rejected)
{
    /// <summary>The validation report written next to the feed.</summary>
    public string ToCsvReport()
    {
        var sb = new StringBuilder("line,code,reason\n");
        foreach (var r in Rejected)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"{r.Line},{Escape(r.Code)},{Escape(r.Reason)}\n");
        }

        return sb.ToString();
    }

    private static string Escape(string? value) =>
        value is null ? ""
            : "\"" + value.Replace("\"", "\"\"",
                StringComparison.Ordinal) + "\"";
}

/// <summary>
/// Validates feed rows, then upserts valid ones in batches by property
/// code. Invalid rows are reported, never silently dropped.
/// </summary>
public sealed class PropertyFeedImporter(BulkWriter writer)
{
    public const int BufferSize = 1000;

    // <listing:feed-import>
    public async Task<FeedImportResult> ImportAsync(
        IAsyncEnumerable<FeedRow> rows,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var rejected = new List<FeedRejection>();
        var buffer = new List<(int Line, Entity Row)>(BufferSize);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int read = 0, imported = 0;

        await foreach (var row in rows
            .WithCancellation(cancellationToken)
            .ConfigureAwait(false))
        {
            read++;
            if (Reject(row, seen) is { } reason)           // <1>
            {
                rejected.Add(new FeedRejection(
                    row.Line, row.Item?.Code, reason));
                continue;
            }

            buffer.Add((row.Line, ToUpsert(row.Item!)));
            if (buffer.Count == BufferSize)                // <2>
            {
                imported += await FlushAsync(
                    buffer, rejected, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        imported += await FlushAsync(
            buffer, rejected, cancellationToken).ConfigureAwait(false);
        return new FeedImportResult(read, imported,
            rejected.OrderBy(r => r.Line).ToList());
    }
    // </listing:feed-import>

    private static string? Reject(FeedRow row, HashSet<string> seen)
    {
        if (row.ParseError is not null)
        {
            return "Unreadable row: " + row.ParseError;
        }

        var errors = PropertyValidator.Validate(row.Item!.ToRequest());
        if (!errors.IsValid)
        {
            return string.Join("; ", errors.ToDictionary()
                .SelectMany(e => e.Value));
        }

        // UpsertMultiple rejects a batch with duplicate keys.
        return seen.Add(row.Item.Code!)
            ? null
            : "Duplicate code in this feed.";
    }

    private static Entity ToUpsert(PropertyFeedItem item)
    {
        var row = PropertyMapper.ToEntity(item.ToRequest());
        row.KeyAttributes[PropertyTable.Code] = item.Code;   // upsert key
        row.Attributes.Remove(PropertyTable.Status);         // keep status
        return row;
    }

    private async Task<int> FlushAsync(
        List<(int Line, Entity Row)> buffer,
        List<FeedRejection> rejected,
        CancellationToken ct)
    {
        if (buffer.Count == 0)
        {
            return 0;
        }

        var result = await writer.UpsertAsync(
            PropertyTable.LogicalName,
            buffer.Select(b => b.Row).ToList(),
            r => r.GetAttributeValue<string>(PropertyTable.Code),
            new BulkOptions(BatchSize: 200, Parallelism: 2),
            ct).ConfigureAwait(false);

        foreach (var error in result.Errors)
        {
            rejected.Add(new FeedRejection(
                buffer[error.Index].Line, error.Key, error.Message));
        }

        buffer.Clear();
        return result.Succeeded;
    }
}
