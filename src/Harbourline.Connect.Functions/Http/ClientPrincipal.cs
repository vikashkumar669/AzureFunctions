using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;

namespace Harbourline.Connect.Functions.Http;

/// <summary>
/// Reads the signed-in user that App Service authentication (Easy Auth)
/// passes to the app in the X-MS-CLIENT-PRINCIPAL header. The platform
/// sets the header after validating the token, and strips any copy a
/// caller sends, so it can be trusted only when Easy Auth is enabled.
/// </summary>
public sealed class ClientPrincipal
{
    private const string ObjectIdClaim =
        "http://schemas.microsoft.com/identity/claims/objectidentifier";

    [JsonPropertyName("claims")]
    public List<PrincipalClaim> Claims { get; set; } = [];

    // <listing:client-principal>
    public static Guid? ObjectIdOf(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var header = request.Headers["X-MS-CLIENT-PRINCIPAL"]
            .ToString();
        if (string.IsNullOrEmpty(header))
        {
            return null;                                   // <1>
        }

        var json = Encoding.UTF8.GetString(
            Convert.FromBase64String(header));             // <2>
        var principal = JsonSerializer
            .Deserialize<ClientPrincipal>(json);

        var oid = principal?.Claims.FirstOrDefault(c =>
            c.Type is ObjectIdClaim or "oid")?.Value;      // <3>
        return Guid.TryParse(oid, out var id) ? id : null;
    }
    // </listing:client-principal>
}

public sealed class PrincipalClaim
{
    [JsonPropertyName("typ")] public string Type { get; set; } = "";
    [JsonPropertyName("val")] public string Value { get; set; } = "";
}
