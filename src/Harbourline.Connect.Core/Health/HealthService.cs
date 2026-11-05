using Harbourline.Connect.Contracts;

namespace Harbourline.Connect.Core.Health;

/// <summary>
/// Builds the health response. Kept free of Azure Functions types
/// so it can be unit tested without the Functions host.
/// </summary>
public sealed class HealthService(TimeProvider timeProvider)
{
    public const string ServiceName = "Harbourline Connect";

    public HealthResponse GetStatus()
    {
        var version = typeof(HealthService).Assembly
            .GetName().Version?.ToString() ?? "0.0.0";

        return new HealthResponse(
            ServiceName,
            version,
            timeProvider.GetUtcNow());
    }
}
