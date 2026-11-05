using System.Net;
using System.Net.Http.Json;

namespace Harbourline.Connect.Core.Integrations;

public interface ILedgerOneClient
{
    Task<LedgerCustomer?> GetCustomerAsync(
        string customerId, CancellationToken cancellationToken);

    Task UpsertCustomerAsync(
        LedgerCustomer customer, string idempotencyKey,
        CancellationToken cancellationToken);

    Task<LedgerCustomerPage> GetCustomersAsync(
        DateTimeOffset modifiedSince, int page,
        CancellationToken cancellationToken);

    Task<LedgerInvoicePage> GetInvoicesAsync(
        DateTimeOffset modifiedSince, int page,
        CancellationToken cancellationToken);

    Task<LedgerOrderReceipt> SubmitOrderAsync(
        LedgerOrder order, CancellationToken cancellationToken);
}

/// <summary>
/// Typed HttpClient for LedgerOne. Base address, API key header and
/// resilience are configured once, in AddHarbourlineIntegrations.
/// </summary>
// <listing:ledger-client>
public sealed class LedgerOneClient(HttpClient http) : ILedgerOneClient
{
    public async Task<LedgerCustomer?> GetCustomerAsync(
        string customerId, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(
            new Uri($"customers/{Uri.EscapeDataString(customerId)}",
                UriKind.Relative),
            cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;                                   // <1>
        }

        response.EnsureSuccessStatusCode();                // <2>
        return await response.Content
            .ReadFromJsonAsync<LedgerCustomer>(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task UpsertCustomerAsync(
        LedgerCustomer customer, string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(customer);
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            new Uri($"customers/{Uri.EscapeDataString(
                customer.CustomerId)}", UriKind.Relative))
        {
            Content = JsonContent.Create(customer),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey); // <3>

        using var response = await http
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }
    // </listing:ledger-client>

    public async Task<LedgerCustomerPage> GetCustomersAsync(
        DateTimeOffset modifiedSince, int page,
        CancellationToken cancellationToken)
    {
        var since = Uri.EscapeDataString(modifiedSince.ToString("O",
            System.Globalization.CultureInfo.InvariantCulture));
        var result = await http.GetFromJsonAsync<LedgerCustomerPage>(
            new Uri($"customers?modifiedSince={since}&page={page}",
                UriKind.Relative),
            cancellationToken).ConfigureAwait(false);
        return result ?? new LedgerCustomerPage([], null);
    }

    public async Task<LedgerInvoicePage> GetInvoicesAsync(
        DateTimeOffset modifiedSince, int page,
        CancellationToken cancellationToken)
    {
        var since = Uri.EscapeDataString(modifiedSince.ToString("O",
            System.Globalization.CultureInfo.InvariantCulture));
        var result = await http.GetFromJsonAsync<LedgerInvoicePage>(
            new Uri($"invoices?modifiedSince={since}&page={page}",
                UriKind.Relative),
            cancellationToken).ConfigureAwait(false);
        return result ?? new LedgerInvoicePage([], null);
    }

    public async Task<LedgerOrderReceipt> SubmitOrderAsync(
        LedgerOrder order, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);
        using var request = new HttpRequestMessage(
            HttpMethod.Post, new Uri("orders", UriKind.Relative))
        {
            Content = JsonContent.Create(order),
        };

        // The order number is unique, so it doubles as the key.
        request.Headers.Add("Idempotency-Key", order.OrderNumber);

        using var response = await http
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return (await response.Content
            .ReadFromJsonAsync<LedgerOrderReceipt>(cancellationToken)
            .ConfigureAwait(false))!;
    }
}
