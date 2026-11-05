using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Harbourline.Connect.Contracts.Properties;

namespace Harbourline.Connect.Core.Properties;

/// <summary>Where the next page starts.</summary>
public sealed record PagePosition(int Page, string? Cookie, string Filter);

/// <summary>
/// Opaque continuation tokens: the caller gets a URL-safe string and
/// sends it back unchanged. The token is tied to the filter it was made
/// for, so it cannot be replayed against a different query.
/// </summary>
public static class ContinuationToken
{
    // <listing:continuation-token>
    public static string Encode(PagePosition position)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(position);
        return Base64Url.EncodeToString(json);             // <1>
    }

    public static PagePosition Decode(string? token, string filter)
    {
        if (string.IsNullOrEmpty(token))
        {
            return new PagePosition(1, null, filter);      // <2>
        }

        PagePosition? position;
        try
        {
            position = JsonSerializer.Deserialize<PagePosition>(
                Base64Url.DecodeFromChars(token));
        }
        catch (Exception ex) when (
            ex is FormatException or JsonException)
        {
            throw new FormatException(
                "The continuation token is not valid.", ex);
        }

        if (position is null || position.Page < 2
            || position.Filter != filter)                  // <3>
        {
            throw new FormatException(
                "The continuation token does not match this query.");
        }

        return position;
    }
    // </listing:continuation-token>

    public static string FilterOf(PropertyQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var text = string.Join('|',
            query.City?.ToUpperInvariant(),
            query.Status?.ToUpperInvariant(),
            query.MinBedrooms,
            query.PageSize);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash, 0, 8);
    }
}
