using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Order.Api.DTOs;
using Test3.Common;
namespace Order.Api.Services;

public sealed class InventoryClient(HttpClient client, IConfiguration config) : IInventoryClient
{
    public async Task<ProductSnapshot> GetAsync(Guid id, string token, string correlationId, CancellationToken ct)
    {
        using var request = Make(HttpMethod.Get, $"api/products/{id}", token, correlationId);
        using var response = await SendAsync(request, ct);
        Check(response);
        try
        {
            var product = await response.Content.ReadFromJsonAsync<ProductSnapshot>(cancellationToken: ct);
            if (product is null || product.ProductId != id || !product.IsActive || product.StockQty < 0) throw Unavailable();
            return product;
        }
        catch (JsonException) { throw Unavailable(); }
    }
    public async Task ChangeAsync(IReadOnlyList<StockItem> items, bool restore, string token, string correlationId, CancellationToken ct)
    {
        using var request = Make(HttpMethod.Post, restore ? "internal/stock/restore" : "internal/stock/deduct", token, correlationId);
        request.Headers.Add("X-Stock-Service-Key", config["InternalStock:Key"]);
        request.Content = JsonContent.Create(new { items });
        using var response = await SendAsync(request, ct);
        Check(response);
    }
    private static HttpRequestMessage Make(HttpMethod method, string path, string token, string correlationId)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Correlation-ID", correlationId);
        return request;
    }
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try { return await client.SendAsync(request, ct); }
        catch (HttpRequestException) { throw Unavailable(); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw Unavailable(); }
    }
    private static void Check(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.NotFound) throw new ApiException(404, "A requested product was not found.");
        if (response.StatusCode == HttpStatusCode.Conflict) throw new ApiException(409, "The requested stock operation conflicts with available stock.");
        if (response.StatusCode != HttpStatusCode.OK) throw Unavailable();
    }
    private static ApiException Unavailable() => new(503, "Inventory Service could not complete the request. A stock mutation may have an uncertain outcome; reconcile before retrying.");
}
