using System.Net.Http.Headers;
using Azure.Core;

namespace Harbourline.Connect.Core.Integrations;

// <listing:bearer-handler>
/// <summary>
/// Adds an Entra ID access token to every outgoing request. The Azure
/// credential caches tokens and refreshes them before they expire.
/// </summary>
public sealed class BearerTokenHandler(
    TokenCredential credential, string scope) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var token = await credential.GetTokenAsync(
            new TokenRequestContext([scope]),
            cancellationToken).ConfigureAwait(false);

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", token.Token);

        return await base.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
    }
}
// </listing:bearer-handler>
