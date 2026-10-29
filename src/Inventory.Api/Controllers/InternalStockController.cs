using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Inventory.Api.DTOs;
using Inventory.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Test3.Common;
namespace Inventory.Api.Controllers;

[ApiController, Authorize(Roles = "USER"), Route("internal/stock")]
public sealed class InternalStockController(ProductService service, IConfiguration config) : ControllerBase
{
    [HttpPost("deduct")]
    public Task<StockOperationResult> Deduct(StockOperationRequest request, CancellationToken ct) => Execute(request, false, ct);
    [HttpPost("restore")]
    public Task<StockOperationResult> Restore(StockOperationRequest request, CancellationToken ct) => Execute(request, true, ct);
    private Task<StockOperationResult> Execute(StockOperationRequest request, bool restore, CancellationToken ct)
    {
        var supplied = SHA256.HashData(Encoding.UTF8.GetBytes(Request.Headers["X-Stock-Service-Key"].ToString()));
        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(config["InternalStock:Key"]!));
        if (!User.HasClaim("service", "order-service") || !CryptographicOperations.FixedTimeEquals(supplied, expected))
            throw new ApiException(403, "This operation is restricted to Order Service.");
        return service.ExecuteOperationAsync(request, Guid.Parse(User.FindFirstValue("sub")!), restore, ct);
    }
}
