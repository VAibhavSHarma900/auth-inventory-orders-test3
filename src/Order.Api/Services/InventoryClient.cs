using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using Order.Api.DTOs;
using Order.Api.Entities;
using Test3.Common;
namespace Order.Api.Services;

public sealed class InventoryClient(HttpClient client, IConfiguration config) : IInventoryClient
{
    public async Task<StockOperationResult> ExecuteAsync(OrderOperation operation, CancellationToken ct)
    {
        // Recovery uses fresh service credentials, never a stored or expired client token.
        var token = new JwtSecurityToken(config["Jwt:Issuer"], config["Jwt:Audience"],
            [new Claim("sub", operation.UserId.ToString()), new Claim("role", "USER"), new Claim("service", "order-service")],
            notBefore: DateTime.UtcNow, expires: DateTime.UtcNow.AddMinutes(1),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!)), SecurityAlgorithms.HmacSha256));
        using var request = new HttpRequestMessage(HttpMethod.Post, operation.OperationType == "CREATE" ? "internal/stock/deduct" : "internal/stock/restore");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        request.Headers.Add("X-Stock-Service-Key", config["InternalStock:Key"]);
        request.Headers.Add("X-Correlation-ID", operation.CorrelationId);
        request.Content = JsonContent.Create(new { operation.OperationId, operation.OrderId, operation.Items });
        try
        {
            using var response = await client.SendAsync(request, ct);
            if (response.StatusCode != HttpStatusCode.OK) throw Unavailable();
            var result = await response.Content.ReadFromJsonAsync<StockOperationResult>(cancellationToken: ct);
            if (result is null || result.OperationId != operation.OperationId ||
                !(result.Outcome == "APPLIED" && result.ErrorStatus is null || result.Outcome == "REJECTED" && result.ErrorStatus is 404 or 409))
                throw Unavailable();
            return result;
        }
        catch (HttpRequestException) { throw Unavailable(); }
        catch (JsonException) { throw Unavailable(); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw Unavailable(); }
    }
    private static ApiException Unavailable() => new(503, "Inventory has not confirmed the operation; durable recovery will retry it safely.");
}
