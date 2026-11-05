using System.Collections.Concurrent;
using System.Globalization;
using Harbourline.Connect.Core.Integrations;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var failureRate = app.Configuration.GetValue("FailureRate", 0.0);
var apiKey = app.Configuration.GetValue("ApiKey", "local-development-key");
var customers = new ConcurrentDictionary<string, LedgerCustomer>();
var orders = new ConcurrentDictionary<string, LedgerOrderReceipt>();
var envelopes = new ConcurrentDictionary<string, Envelope>();

// <listing:mock-failures>
// Every request: check the API key, then fail some on purpose so the
// resilience pipeline in Chapter 8 has something to do.
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? "";
    if (path.StartsWith("/ledgerone", StringComparison.Ordinal)
        && context.Request.Headers["x-api-key"] != apiKey)
    {
        context.Response.StatusCode = 401;                 // <1>
        return;
    }

    if (Random.Shared.NextDouble() < failureRate)
    {
        context.Response.StatusCode = Random.Shared.Next(2) == 0
            ? 503 : 429;                                   // <2>
        context.Response.Headers.RetryAfter = "1";
        return;
    }

    await next(context);
});
// </listing:mock-failures>

var ledger = app.MapGroup("/ledgerone");
ledger.MapGet("/customers/{id}", (string id) =>
    customers.TryGetValue(id, out var c)
        ? Results.Ok(c) : Results.NotFound());
ledger.MapPut("/customers/{id}", (string id, LedgerCustomer c) =>
{
    customers[id] = c with { ModifiedUtc = DateTimeOffset.UtcNow };
    return Results.Ok(customers[id]);
});
ledger.MapGet("/customers", (string? modifiedSince, int? page) =>
{
    var since = Since(modifiedSince);
    var items = customers.Values.Where(c => c.ModifiedUtc >= since)
        .OrderBy(c => c.CustomerId, StringComparer.Ordinal).ToList();
    return Results.Ok(new LedgerCustomerPage(items, null));
});
ledger.MapGet("/invoices", (string? modifiedSince, int? page) =>
{
    // Three fixed sample invoices (fictional data).
    var day = new DateOnly(2026, 10, 1);
    LedgerInvoice[] items =
    [
        new("INV-10001", "LSE-0001", 1250m, day, "Open", DateTimeOffset.UtcNow),
        new("INV-10002", "LSE-0002", 980m, day, "Paid", DateTimeOffset.UtcNow),
        new("INV-10003", "LSE-0003", 1430m, day, "Overdue", DateTimeOffset.UtcNow),
    ];
    return Results.Ok(new LedgerInvoicePage(
        items.Where(i => i.ModifiedUtc >= Since(modifiedSince)).ToList(),
        null));
});

// <listing:mock-idempotency>
ledger.MapPost("/orders", (HttpRequest request, LedgerOrder order) =>
{
    var key = request.Headers["Idempotency-Key"].ToString();
    if (string.IsNullOrEmpty(key))
    {
        return Results.BadRequest("Idempotency-Key is required.");
    }

    var receipt = orders.GetOrAdd(key, _ =>                // <1>
        new LedgerOrderReceipt("L1-" + Guid.NewGuid().ToString("N")[..8]));
    return Results.Created($"/ledgerone/orders/{receipt.LedgerOrderId}",
        receipt);                                          // <2>
});
// </listing:mock-idempotency>

var sign = app.MapGroup("/signbridge");
sign.MapPost("/envelopes", (EnvelopeRequest request) =>
{
    var envelope = new Envelope("ENV-" + Guid.NewGuid().ToString("N")[..10],
        "Sent");
    envelopes[envelope.EnvelopeId] = envelope;
    return Results.Created($"/signbridge/envelopes/{envelope.EnvelopeId}",
        envelope);
});
sign.MapPost("/envelopes/{id}/reminders", (string id) =>
    envelopes.ContainsKey(id) ? Results.Accepted() : Results.NotFound());

app.Run();

static DateTimeOffset Since(string? value) =>
    value is null ? DateTimeOffset.MinValue
        : DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
