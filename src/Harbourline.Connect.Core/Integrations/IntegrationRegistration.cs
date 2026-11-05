using Azure.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace Harbourline.Connect.Core.Integrations;

public static class IntegrationRegistration
{
    /// <summary>Registers the LedgerOne and SignBridge clients.</summary>
    public static IServiceCollection AddHarbourlineIntegrations(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<LedgerOneOptions>()
            .Bind(configuration.GetSection(LedgerOneOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<SignBridgeOptions>()
            .Bind(configuration.GetSection(SignBridgeOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // <listing:ledger-registration>
        services
            .AddHttpClient<ILedgerOneClient, LedgerOneClient>(
                (sp, http) =>
                {
                    var o = sp.GetRequiredService<
                        IOptions<LedgerOneOptions>>().Value;
                    http.BaseAddress = new Uri(
                        o.BaseUrl.TrimEnd('/') + "/");      // <1>
                    http.DefaultRequestHeaders.Add(
                        "x-api-key", o.ApiKey);             // <2>
                })
            .AddStandardResilienceHandler(r =>              // <3>
            {
                r.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
                r.TotalRequestTimeout.Timeout =
                    TimeSpan.FromSeconds(45);
                r.Retry.MaxRetryAttempts = 3;               // <4>
                r.CircuitBreaker.SamplingDuration =
                    TimeSpan.FromSeconds(30);
            });
        // </listing:ledger-registration>

        // <listing:signbridge-registration>
        services
            .AddHttpClient<ISignBridgeClient, SignBridgeClient>(
                (sp, http) =>
                {
                    var o = sp.GetRequiredService<
                        IOptions<SignBridgeOptions>>().Value;
                    http.BaseAddress = new Uri(
                        o.BaseUrl.TrimEnd('/') + "/");
                })
            .AddHttpMessageHandler(sp =>
                new BearerTokenHandler(                     // <1>
                    sp.GetRequiredService<TokenCredential>(),
                    sp.GetRequiredService<
                        IOptions<SignBridgeOptions>>().Value.Scope))
            .AddStandardResilienceHandler(r =>
            {
                // Creating an envelope is not idempotent: never
                // retry POST, or a tenant could get two envelopes.
                r.Retry.DisableForUnsafeHttpMethods();      // <2>
            });
        // </listing:signbridge-registration>

        return services;
    }
}
