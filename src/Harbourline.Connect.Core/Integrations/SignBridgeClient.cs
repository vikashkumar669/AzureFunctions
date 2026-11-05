using System.ComponentModel.DataAnnotations;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Harbourline.Connect.Core.Integrations;

// SignBridge is the fictional e-signature service. It is protected by
// Microsoft Entra ID, so Harbourline calls it with a token for its
// managed identity: no API key at all.

public sealed class SignBridgeOptions
{
    public const string SectionName = "SignBridge";

    [Required, Url]
    public string BaseUrl { get; set; } = "";

    /// <summary>Entra ID scope, for example api://signbridge/.default.</summary>
    [Required]
    public string Scope { get; set; } = "";

    /// <summary>Shared secret for callback signatures (Key Vault).</summary>
    [Required, MinLength(32)]
    public string CallbackSecret { get; set; } = "";
}

public sealed record EnvelopeRequest(
    [property: JsonPropertyName("reference")] string Reference,
    [property: JsonPropertyName("signerName")] string SignerName,
    [property: JsonPropertyName("signerEmail")] string SignerEmail,
    [property: JsonPropertyName("documentName")] string DocumentName);

public sealed record Envelope(
    [property: JsonPropertyName("envelopeId")] string EnvelopeId,
    [property: JsonPropertyName("status")] string Status);

public interface ISignBridgeClient
{
    Task<Envelope> CreateEnvelopeAsync(
        EnvelopeRequest request, CancellationToken cancellationToken);

    Task SendReminderAsync(
        string envelopeId, CancellationToken cancellationToken);
}

public sealed class SignBridgeClient(HttpClient http) : ISignBridgeClient
{
    public async Task<Envelope> CreateEnvelopeAsync(
        EnvelopeRequest request, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync(
            new Uri("envelopes", UriKind.Relative), request,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return (await response.Content
            .ReadFromJsonAsync<Envelope>(cancellationToken)
            .ConfigureAwait(false))!;
    }

    public async Task SendReminderAsync(
        string envelopeId, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsync(
            new Uri($"envelopes/{Uri.EscapeDataString(envelopeId)}"
                + "/reminders", UriKind.Relative),
            content: null, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }
}
