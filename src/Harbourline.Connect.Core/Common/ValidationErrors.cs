namespace Harbourline.Connect.Core.Common;

/// <summary>Collects field errors in the shape ProblemDetails uses.</summary>
public sealed class ValidationErrors
{
    private readonly Dictionary<string, List<string>> errors =
        new(StringComparer.OrdinalIgnoreCase);

    public bool IsValid => errors.Count == 0;

    public void Add(string field, string message)
    {
        if (!errors.TryGetValue(field, out var list))
        {
            list = [];
            errors[field] = list;
        }

        list.Add(message);
    }

    public void Require(string field, string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Add(field, $"{field} is required.");
        }
        else if (value.Length > maxLength)
        {
            Add(field, $"{field} must be {maxLength} characters or fewer.");
        }
    }

    public IReadOnlyDictionary<string, string[]> ToDictionary() =>
        errors.ToDictionary(
            e => e.Key, e => e.Value.ToArray(),
            StringComparer.OrdinalIgnoreCase);
}
