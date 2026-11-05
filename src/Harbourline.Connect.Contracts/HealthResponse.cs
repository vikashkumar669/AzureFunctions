namespace Harbourline.Connect.Contracts;

/// <summary>
/// Response body of <c>GET /api/health</c>.
/// </summary>
/// <param name="Service">Name of the service that answered.</param>
/// <param name="Version">Assembly version of the deployed build.</param>
/// <param name="CheckedAtUtc">When the check ran, in UTC.</param>
public sealed record HealthResponse(
    string Service,
    string Version,
    DateTimeOffset CheckedAtUtc);
