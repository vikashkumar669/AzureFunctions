using System.Net;
using System.Text;
using Harbourline.Connect.Core.Integrations;

namespace Harbourline.Connect.UnitTests.Integrations;

public sealed class LedgerOneClientTests
{
    /// <summary>Answers every request with a fixed response.</summary>
    private sealed class StubHandler(HttpStatusCode status, string body)
        : HttpMessageHandler
    {
        public HttpRequestMessage? Last { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Last = request;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(
                    body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static (LedgerOneClient, StubHandler) Create(
        HttpStatusCode status, string body = "{}")
    {
        var handler = new StubHandler(status, body);
        var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://ledgerone.example/api/"),
        };
        return (new LedgerOneClient(http), handler);
    }

    [Fact]
    public async Task MissingCustomer_ReturnsNull()
    {
        var (client, _) = Create(HttpStatusCode.NotFound);
        Assert.Null(await client.GetCustomerAsync(
            "C-1", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ServerError_Throws()
    {
        var (client, _) = Create(HttpStatusCode.ServiceUnavailable);
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.GetCustomerAsync(
                "C-1", TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
    }

    [Fact]
    public async Task Upsert_SendsIdempotencyKey()
    {
        var (client, handler) = Create(HttpStatusCode.OK);
        await client.UpsertCustomerAsync(
            new LedgerCustomer("C-1", "Northwind Lettings (fictional)",
                null, null, "Leeds", DateTimeOffset.UnixEpoch),
            "C-1-ABCD", TestContext.Current.CancellationToken);

        Assert.Equal(HttpMethod.Put, handler.Last!.Method);
        Assert.Equal("https://ledgerone.example/api/customers/C-1",
            handler.Last.RequestUri!.ToString());
        Assert.Equal("C-1-ABCD",
            handler.Last.Headers.GetValues("Idempotency-Key").Single());
    }
}
