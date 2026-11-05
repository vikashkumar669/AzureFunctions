using System.Text.RegularExpressions;
using Harbourline.Connect.Contracts.Properties;
using Harbourline.Connect.Core.Common;
using Harbourline.Connect.Core.Dataverse;

namespace Harbourline.Connect.Core.Properties;

/// <summary>
/// Validates requests before anything is sent to Dataverse, so bad input
/// costs no API calls and returns every problem at once.
/// </summary>
public static partial class PropertyValidator
{
    public const decimal MaxMonthlyRent = 100_000m;

    // Property codes look like "HBR-0042": three letters, four digits.
    [GeneratedRegex("^[A-Z]{3}-[0-9]{4}$")]
    private static partial Regex CodePattern();

    public static bool IsValidCode(string? code) =>
        code is not null && CodePattern().IsMatch(code);

    // <listing:property-validate>
    public static ValidationErrors Validate(CreatePropertyRequest r)
    {
        ArgumentNullException.ThrowIfNull(r);
        var errors = new ValidationErrors();

        if (!IsValidCode(r.Code))
        {
            errors.Add("code", "code must look like ABC-1234.");
        }

        errors.Require("name", r.Name, 100);
        errors.Require("city", r.City, 60);
        errors.Require("postcode", r.Postcode, 10);

        if (r.Bedrooms is null or < 0 or > 20)
        {
            errors.Add("bedrooms", "bedrooms must be 0 to 20.");
        }

        if (r.MonthlyRent is null or <= 0 or > MaxMonthlyRent)
        {
            errors.Add("monthlyRent",
                $"monthlyRent must be above 0 and at most {MaxMonthlyRent}.");
        }

        return errors;
    }
    // </listing:property-validate>

    public static ValidationErrors Validate(UpdatePropertyRequest r)
    {
        ArgumentNullException.ThrowIfNull(r);
        var errors = new ValidationErrors();

        if (r is { Name: null, Bedrooms: null, MonthlyRent: null,
                   Status: null, AvailableFrom: null })
        {
            errors.Add("body", "Send at least one field to change.");
        }

        if (r.Name is not null)
        {
            errors.Require("name", r.Name, 100);
        }

        if (r.Bedrooms is < 0 or > 20)
        {
            errors.Add("bedrooms", "bedrooms must be 0 to 20.");
        }

        if (r.MonthlyRent is <= 0 or > MaxMonthlyRent)
        {
            errors.Add("monthlyRent",
                $"monthlyRent must be above 0 and at most {MaxMonthlyRent}.");
        }

        if (r.Status is not null && !TryParseStatus(r.Status, out _))
        {
            errors.Add("status",
                "status must be Available, Let or Maintenance.");
        }

        return errors;
    }

    public static bool TryParseStatus(string value, out PropertyStatus status)
    {
        // Accept names only: Enum.TryParse would also accept "100000000".
        foreach (var candidate in Enum.GetValues<PropertyStatus>())
        {
            if (string.Equals(candidate.ToString(), value,
                    StringComparison.OrdinalIgnoreCase))
            {
                status = candidate;
                return true;
            }
        }

        status = default;
        return false;
    }
}
