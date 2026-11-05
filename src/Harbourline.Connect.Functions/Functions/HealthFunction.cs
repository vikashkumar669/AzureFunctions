using Harbourline.Connect.Core.Health;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace Harbourline.Connect.Functions.Functions;

/// <summary>
/// GET /api/health – a liveness check that does not touch Dataverse,
/// so it stays green even when a downstream system is unavailable.
/// </summary>
// <listing:health-function>
public sealed class HealthFunction(HealthService health)    // <1>
{
    [Function("Health")]                                    // <2>
    public IActionResult Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "health")] HttpRequest request)         // <3>
    {
        ArgumentNullException.ThrowIfNull(request);
        return new OkObjectResult(health.GetStatus());      // <4>
    }
}
// </listing:health-function>
