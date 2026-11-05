using System.Diagnostics;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;

namespace Harbourline.Connect.Functions.Middleware;

/// <summary>
/// Adds the invocation id, function name and trace id to every log
/// written during an invocation, so one KQL filter finds them all
/// (Chapter 18).
/// </summary>
// <listing:middleware>
public sealed class CorrelationMiddleware(
    ILogger<CorrelationMiddleware> logger) : IFunctionsWorkerMiddleware
{
    public async Task Invoke(
        FunctionContext context, FunctionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        using (logger.BeginScope(new Dictionary<string, object?>
        {
            ["InvocationId"] = context.InvocationId,       // <1>
            ["FunctionName"] = context.FunctionDefinition.Name,
            ["TraceId"] = Activity.Current?.TraceId.ToString(), // <2>
        }))
        {
            await next(context);                           // <3>
        }
    }
}
// </listing:middleware>
