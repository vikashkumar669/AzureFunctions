using System.Globalization;
using Harbourline.Connect.Core.Dataverse;
using Microsoft.Xrm.Sdk;

namespace Harbourline.Connect.Core.Migration;

/// <summary>A row exported from Harbourline's old lettings system.</summary>
public sealed record LegacyPropertyRow(
    string PropRef,
    string AddressLine,
    string Town,
    string PostCode,
    string Beds,
    string RentPcm,
    string State,
    string AvailDate);

/// <summary>A mapped row, or the reason it could not be mapped.</summary>
public sealed record MappedRow(
    LegacyPropertyRow Source, Entity? Row, string? Error)
{
    public string Code => LegacyMapping.CodeOf(Source.PropRef);
}

/// <summary>
/// The mapping specification of Chapter 16, as code. Each rule here is
/// one row of the spec table, so a reviewer can check them side by side.
/// </summary>
public static class LegacyMapping
{
    private static readonly CultureInfo UkCulture =
        CultureInfo.GetCultureInfo("en-GB");

    // <listing:legacy-map>
    public static string CodeOf(string propRef) =>
        "HBR-" + propRef.Trim().PadLeft(4, '0');           // <1>

    public static PropertyStatus? StatusOf(string state) =>
        state.Trim().ToUpperInvariant() switch
        {
            "A" => PropertyStatus.Available,
            "L" => PropertyStatus.Let,
            "M" or "V" => PropertyStatus.Maintenance,      // <2>
            _ => null,
        };

    public static MappedRow Map(LegacyPropertyRow source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (StatusOf(source.State) is not { } status)
        {
            return new MappedRow(source, null,
                $"Unknown state '{source.State}'.");       // <3>
        }

        if (!int.TryParse(source.Beds, NumberStyles.None,
                CultureInfo.InvariantCulture, out var beds)
            || !decimal.TryParse(source.RentPcm, NumberStyles.Number,
                UkCulture, out var rent))
        {
            return new MappedRow(source, null,
                "Bedrooms or rent is not a number.");
        }

        var code = CodeOf(source.PropRef);
        var row = new Entity(PropertyTable.LogicalName,
            PropertyTable.Code, code)                      // <4>
        {
            [PropertyTable.Code] = code,
            [PropertyTable.Name] = source.AddressLine.Trim(),
            [PropertyTable.City] = UkCulture.TextInfo
                .ToTitleCase(source.Town.Trim().ToLowerInvariant()),
            [PropertyTable.Postcode] =
                source.PostCode.Trim().ToUpperInvariant(),
            [PropertyTable.Bedrooms] = beds,
            [PropertyTable.MonthlyRent] = new Money(rent),
            [PropertyTable.Status] = new OptionSetValue((int)status),
        };

        if (DateOnly.TryParseExact(source.AvailDate, "dd/MM/yyyy",
                CultureInfo.InvariantCulture, DateTimeStyles.None,
                out var from))
        {
            row[PropertyTable.AvailableFrom] =
                from.ToDateTime(TimeOnly.MinValue);
        }

        return new MappedRow(source, row, null);
    }
    // </listing:legacy-map>
}
