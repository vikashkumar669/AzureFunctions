using Harbourline.Connect.Core.Search;
using Harbourline.Connect.Functions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Net.Http.Headers;

namespace Harbourline.Connect.Functions.Search;

/// <summary>Public property search, published through APIM (Chapter 23).</summary>
public sealed class PropertySearchFunction(PropertySearch search)
{
    // <listing:search-function>
    [Function("SearchProperties")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get",
            Route = "v1/search/properties")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var q = request.Query;
        try
        {
            var page = await search.SearchAsync(
                city: q["city"],
                minBedrooms: int.TryParse(q["minBedrooms"], out var b)
                    ? b : null,
                after: q["after"],
                size: int.TryParse(q["size"], out var s) ? s : 20,
                cancellationToken);

            request.HttpContext.Response.Headers[
                HeaderNames.CacheControl] = "public, max-age=60"; // <1>
            return new OkObjectResult(page);
        }
        catch (FormatException ex)
        {
            return ApiResults.Problem(request, 400,
                "Invalid cursor", ex.Message);
        }
    }
    // </listing:search-function>
}
