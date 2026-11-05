using System.Text.Json;
using Harbourline.Connect.Contracts.Properties;
using Harbourline.Connect.Core.Properties;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Net.Http.Headers;

namespace Harbourline.Connect.Functions.Http;

/// <summary>
/// Version 1 of the Property API (Chapters 6 and 7):
///   GET    /api/v1/properties          list, filtered and paged
///   GET    /api/v1/properties/{code}   one property, with an ETag
///   POST   /api/v1/properties          create
///   PATCH  /api/v1/properties/{code}   change some fields (If-Match)
/// </summary>
public sealed class PropertyFunctions(PropertyService properties)
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web);

    // <listing:fn-list>
    [Function("ListProperties")]
    public async Task<IActionResult> List(
        [HttpTrigger(AuthorizationLevel.Function, "get",
            Route = "v1/properties")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var q = request.Query;
        var query = new PropertyQuery(
            City: q["city"],
            Status: q["status"],
            MinBedrooms: int.TryParse(q["minBedrooms"], out var b)
                ? b : null,
            PageSize: int.TryParse(q["pageSize"], out var s)
                ? s : 50);                                 // <1>

        try
        {
            var page = await properties.ListAsync(
                query, q["continuationToken"], cancellationToken);
            return new OkObjectResult(page);
        }
        catch (FormatException ex)                         // <2>
        {
            return ApiResults.Problem(request,
                StatusCodes.Status400BadRequest,
                "Invalid continuation token", ex.Message);
        }
    }
    // </listing:fn-list>

    // <listing:fn-get>
    [Function("GetProperty")]
    public async Task<IActionResult> Get(
        [HttpTrigger(AuthorizationLevel.Function, "get",
            Route = "v1/properties/{code}")] HttpRequest request,
        string code,                                       // <1>
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var property = await properties.GetByCodeAsync(
            code, cancellationToken);
        if (property is null)
        {
            return ApiResults.Problem(request,
                StatusCodes.Status404NotFound, "Not found",
                $"Property {code} was not found.");
        }

        SetETag(request, property);                        // <2>
        return new OkObjectResult(property);
    }
    // </listing:fn-get>

    [Function("CreateProperty")]
    public async Task<IActionResult> Create(
        [HttpTrigger(AuthorizationLevel.Function, "post",
            Route = "v1/properties")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = await ReadAsync<CreatePropertyRequest>(
            request, cancellationToken);
        if (body is null)
        {
            return InvalidJson(request);
        }

        var outcome = await properties.CreateAsync(
            body, cancellationToken);
        if (outcome.Value is { } created)
        {
            SetETag(request, created);
        }

        return ApiResults.From(outcome, request,
            p => $"/api/v1/properties/{p.Code}");
    }

    // <listing:fn-patch>
    [Function("UpdateProperty")]
    public async Task<IActionResult> Update(
        [HttpTrigger(AuthorizationLevel.Function, "patch",
            Route = "v1/properties/{code}")] HttpRequest request,
        string code,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = await ReadAsync<UpdatePropertyRequest>(
            request, cancellationToken);
        if (body is null)
        {
            return InvalidJson(request);
        }

        var ifMatch = request.Headers.IfMatch.ToString();  // <1>
        var version = string.IsNullOrEmpty(ifMatch)
            ? null
            : ifMatch.Trim('"');

        var outcome = await properties.UpdateAsync(
            code, body, version, cancellationToken);
        if (outcome.Value is { } updated)
        {
            SetETag(request, updated);                     // <2>
        }

        return ApiResults.From(outcome, request);
    }
    // </listing:fn-patch>

    private static void SetETag(HttpRequest request, PropertyDto p)
    {
        if (p.Version is not null)
        {
            request.HttpContext.Response.Headers[HeaderNames.ETag] =
                $"\"{p.Version}\"";
        }
    }

    private static async Task<T?> ReadAsync<T>(
        HttpRequest request, CancellationToken ct)
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(
                request.Body, Json, ct);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static ObjectResult InvalidJson(HttpRequest request) =>
        ApiResults.Problem(request, StatusCodes.Status400BadRequest,
            "Invalid JSON", "The request body is not valid JSON.");
}
