using Harbourline.Connect.Core.Feeds;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Harbourline.Connect.Functions.Feeds;

public sealed class FeedOutputs
{
    /// <summary>The validation report, written next to every feed.</summary>
    [BlobOutput("feed-reports/{name}.report.csv",
        Connection = "FeedStorage")]
    public string? Report { get; set; }

    /// <summary>A copy of a file that could not be read at all.</summary>
    [BlobOutput("feed-quarantine/{name}", Connection = "FeedStorage")]
    public byte[]? Quarantined { get; set; }
}

/// <summary>
/// Imports partner property feeds dropped into the property-feeds
/// container (Chapter 13). Event Grid delivers the blob event, so the
/// function starts within seconds, even on Flex Consumption.
/// </summary>
public sealed partial class PropertyFeedFunction(
    PropertyFeedImporter importer,
    ILogger<PropertyFeedFunction> logger)
{
    // <listing:blob-function>
    [Function("ImportPropertyFeed")]
    public async Task<FeedOutputs> Run(
        [BlobTrigger("property-feeds/{name}",
            Source = BlobTriggerSource.EventGrid,          // <1>
            Connection = "FeedStorage")] byte[] content,
        string name,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        try
        {
            using var stream = new MemoryStream(content);
            var rows = PropertyFeedReader.ReadAsync(
                name, stream, cancellationToken);          // <2>
            var result = await importer.ImportAsync(
                rows, cancellationToken);

            LogImported(logger, name, result.Read,
                result.Imported, result.Rejected.Count);
            return new FeedOutputs
            {
                Report = result.ToCsvReport(),             // <3>
            };
        }
        catch (Exception ex) when (ex is NotSupportedException
            or System.Text.Json.JsonException
            or System.Xml.XmlException
            or CsvHelper.CsvHelperException)
        {
            LogQuarantined(logger, ex, name);
            return new FeedOutputs { Quarantined = content }; // <4>
        }
    }
    // </listing:blob-function>

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Feed {Name}: {Read} read, {Imported} imported, "
            + "{Rejected} rejected")]
    private static partial void LogImported(ILogger logger,
        string name, int read, int imported, int rejected);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Feed {Name} could not be read and was quarantined")]
    private static partial void LogQuarantined(
        ILogger logger, Exception exception, string name);
}
