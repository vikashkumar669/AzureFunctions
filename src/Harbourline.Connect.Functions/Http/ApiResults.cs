using Harbourline.Connect.Core.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Harbourline.Connect.Functions.Http;

/// <summary>
/// Turns service outcomes into HTTP responses. Errors use RFC 9457
/// problem details, so every client parses failures the same way.
/// </summary>
public static class ApiResults
{
    // <listing:api-results>
    public static IActionResult From<T>(
        Outcome<T> outcome,
        HttpRequest request,
        Func<T, string>? location = null)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(request);

        return outcome.Status switch
        {
            OutcomeStatus.Ok => new OkObjectResult(outcome.Value),
            OutcomeStatus.Created => new CreatedResult(
                location?.Invoke(outcome.Value!), outcome.Value),
            OutcomeStatus.Invalid => new BadRequestObjectResult(
                new ValidationProblemDetails(                // <1>
                    outcome.Errors!.ToDictionary())
                {
                    Title = "The request is not valid.",
                    Instance = request.Path,
                }),
            OutcomeStatus.NotFound => Problem(request,
                StatusCodes.Status404NotFound, "Not found",
                outcome.Detail),
            OutcomeStatus.Conflict => Problem(request,
                StatusCodes.Status409Conflict, "Conflict",
                outcome.Detail),
            OutcomeStatus.PreconditionFailed => Problem(request,
                StatusCodes.Status412PreconditionFailed,  // <2>
                "Precondition failed", outcome.Detail),
            _ => throw new InvalidOperationException(
                $"Unhandled outcome {outcome.Status}."),
        };
    }

    public static ObjectResult Problem(
        HttpRequest request, int status, string title, string? detail)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new ObjectResult(new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Instance = request.Path,                       // <3>
        })
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" },
        };
    }
    // </listing:api-results>
}
