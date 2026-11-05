using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using Harbourline.Connect.Core.Dataverse;
using Microsoft.Xrm.Sdk;

namespace Harbourline.Connect.Core.Bulk;

/// <summary>A line of a smart-meter export: the Chapter 15 workload.</summary>
public sealed record MeterReadingLine(
    string PropertyCode, string Meter, DateTime ReadAt, decimal Value);

public static class MeterReadings
{
    // <listing:meter-map>
    public static Entity ToEntity(MeterReadingLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var key = string.Create(CultureInfo.InvariantCulture,
            $"{line.PropertyCode}|{line.Meter}|{line.ReadAt:O}"); // <1>

        return new Entity(MeterReadingTable.LogicalName,
            MeterReadingTable.ReadingKey, key)
        {
            [MeterReadingTable.ReadingKey] = key,
            [MeterReadingTable.Name] = key,
            [MeterReadingTable.Property] = new EntityReference(
                PropertyTable.LogicalName,
                PropertyTable.Code, line.PropertyCode),    // <2>
            [MeterReadingTable.Meter] = line.Meter,
            [MeterReadingTable.ReadAt] = line.ReadAt,
            [MeterReadingTable.Value] = line.Value,
        };
    }
    // </listing:meter-map>

    public static List<MeterReadingLine> ReadCsv(Stream content)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            PrepareHeaderForMatch = a => a.Header.ToUpperInvariant(),
        };
        using var reader = new StreamReader(content);
        using var csv = new CsvReader(reader, config);
        return csv.GetRecords<MeterReadingLine>().ToList();
    }
}
