using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using CsvHelper;
using CsvHelper.Configuration;
using Harbourline.Connect.Contracts.Properties;

namespace Harbourline.Connect.Core.Feeds;

/// <summary>One row of a partner's property feed, as received.</summary>
public sealed class PropertyFeedItem
{
    [JsonPropertyName("code")] public string? Code { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("city")] public string? City { get; set; }
    [JsonPropertyName("postcode")] public string? Postcode { get; set; }
    [JsonPropertyName("bedrooms")] public int? Bedrooms { get; set; }
    [JsonPropertyName("monthlyRent")] public decimal? MonthlyRent { get; set; }
    [JsonPropertyName("availableFrom")] public DateOnly? AvailableFrom { get; set; }

    public CreatePropertyRequest ToRequest() => new(
        Code, Name, City, Postcode, Bedrooms, MonthlyRent, AvailableFrom);
}

/// <summary>A row plus where it came from, for the error report.</summary>
public sealed record FeedRow(int Line, PropertyFeedItem? Item, string? ParseError);

/// <summary>
/// Streams property feeds in CSV, JSON or XML without loading the whole
/// file into memory (Chapter 13). Rows that cannot be read are returned
/// with a ParseError instead of stopping the import.
/// </summary>
public static class PropertyFeedReader
{
    public static IAsyncEnumerable<FeedRow> ReadAsync(
        string fileName, Stream content, CancellationToken ct) =>
        Path.GetExtension(fileName).ToUpperInvariant() switch
        {
            ".CSV" => ReadCsvAsync(content, ct),
            ".JSON" => ReadJsonAsync(content, ct),
            ".XML" => ReadXmlAsync(content, ct),
            _ => throw new NotSupportedException(
                $"Unsupported feed type: {fileName}"),
        };

    // <listing:feed-csv>
    public static async IAsyncEnumerable<FeedRow> ReadCsvAsync(
        Stream content,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            PrepareHeaderForMatch = a => a.Header.ToUpperInvariant(),
            MissingFieldFound = null,                      // <1>
            HeaderValidated = null,
        };
        using var reader = new StreamReader(content);
        using var csv = new CsvReader(reader, config);

        await csv.ReadAsync().ConfigureAwait(false);
        csv.ReadHeader();

        while (await csv.ReadAsync().ConfigureAwait(false))   // <2>
        {
            ct.ThrowIfCancellationRequested();
            var line = csv.Parser.RawRow;
            FeedRow row;
            try
            {
                row = new FeedRow(line,
                    csv.GetRecord<PropertyFeedItem>(), null);
            }
            catch (CsvHelperException ex)                  // <3>
            {
                row = new FeedRow(line, null,
                    ex.InnerException?.Message ?? ex.Message);
            }

            yield return row;
        }
    }
    // </listing:feed-csv>

    // <listing:feed-json>
    public static async IAsyncEnumerable<FeedRow> ReadJsonAsync(
        Stream content,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var line = 0;
        await foreach (var item in JsonSerializer
            .DeserializeAsyncEnumerable<PropertyFeedItem>(
                content, cancellationToken: ct)
            .ConfigureAwait(false))                        // <1>
        {
            line++;
            yield return new FeedRow(line, item, null);
        }
    }
    // </listing:feed-json>

    public static async IAsyncEnumerable<FeedRow> ReadXmlAsync(
        Stream content,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var settings = new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Prohibit,        // no XXE
            IgnoreWhitespace = true,
        };
        using var xml = XmlReader.Create(content, settings);
        var line = 0;

        while (await xml.ReadAsync().ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            if (xml.NodeType != XmlNodeType.Element
                || xml.Name != "property")
            {
                continue;
            }

            line++;
            FeedRow row;
            try
            {
                row = new FeedRow(line, new PropertyFeedItem
                {
                    Code = xml.GetAttribute("code"),
                    Name = xml.GetAttribute("name"),
                    City = xml.GetAttribute("city"),
                    Postcode = xml.GetAttribute("postcode"),
                    Bedrooms = ParseInt(xml.GetAttribute("bedrooms")),
                    MonthlyRent =
                        ParseDecimal(xml.GetAttribute("monthlyRent")),
                    AvailableFrom =
                        ParseDate(xml.GetAttribute("availableFrom")),
                }, null);
            }
            catch (FormatException ex)
            {
                row = new FeedRow(line, null, ex.Message);
            }

            yield return row;
        }
    }

    private static int? ParseInt(string? s) =>
        s is null ? null : int.Parse(s, CultureInfo.InvariantCulture);

    private static decimal? ParseDecimal(string? s) =>
        s is null ? null
            : decimal.Parse(s, CultureInfo.InvariantCulture);

    private static DateOnly? ParseDate(string? s) =>
        s is null ? null
            : DateOnly.ParseExact(s, "yyyy-MM-dd",
                CultureInfo.InvariantCulture);
}
