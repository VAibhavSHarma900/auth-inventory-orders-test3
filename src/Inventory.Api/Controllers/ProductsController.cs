using Inventory.Api.DTOs;
using Inventory.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Test3.Common;
namespace Inventory.Api.Controllers;

[ApiController, Authorize(Roles = "ADMIN,USER"), Route("api/products")]
public sealed class ProductsController(ProductService service) : ControllerBase
{
    [HttpPost, Authorize(Roles = "ADMIN")]
    public async Task<ActionResult<ProductResponse>> Create(ProductRequest request, CancellationToken ct)
    {
        var product = await service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = product.ProductId }, product);
    }
    [HttpGet("{id:guid}")]
    public Task<ProductResponse> Get(Guid id, CancellationToken ct) => service.GetAsync(id, ct);
    [HttpGet]
    public Task<PagedResponse<ProductResponse>> List([FromQuery] PageQuery page, CancellationToken ct) => service.ListAsync(page, ct);
    [HttpPut("{id:guid}"), Authorize(Roles = "ADMIN")]
    public Task<ProductResponse> Update(Guid id, ProductRequest request, CancellationToken ct) => service.UpdateAsync(id, request, ct);
    [HttpDelete("{id:guid}"), Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await service.DeleteAsync(id, ct);
        return NoContent();
    }
    [HttpPost("{id:guid}/reduce_stock"), Authorize(Roles = "ADMIN")]
    public async Task<StockResponse> Reduce(Guid id, QuantityRequest request, CancellationToken ct)
    {
        if (id == Guid.Empty) throw new ApiException(400, "ProductId cannot be empty.");
        var result = await service.ChangeStockAsync(new StockBatchRequest { Items = [new StockItem { ProductId = id, Quantity = request.Quantity }] }, false, ct);
        return result[0];
    }
}
