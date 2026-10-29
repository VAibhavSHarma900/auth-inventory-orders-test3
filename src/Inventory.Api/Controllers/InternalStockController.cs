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
    public Task<IReadOnlyList<StockResponse>> Deduct(StockBatchRequest request, CancellationToken ct)
    {
        CheckServiceKey();
        return service.ChangeStockAsync(request, false, ct);
    }
    [HttpPost("restore")]
    public Task<IReadOnlyList<StockResponse>> Restore(StockBatchRequest request, CancellationToken ct)
    {
        CheckServiceKey();
        return service.ChangeStockAsync(request, true, ct);
    }
    private void CheckServiceKey()
    {
        var supplied = SHA256.HashData(Encoding.UTF8.GetBytes(Request.Headers["X-Stock-Service-Key"].ToString()));
        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(config["InternalStock:Key"]!));
        if (!CryptographicOperations.FixedTimeEquals(supplied, expected))
            throw new ApiException(403, "This operation is restricted to Order Service.");
    }
}
