using Inventory.Api.DTOs;
using Inventory.Api.Repositories;
using Test3.Common;
namespace Inventory.Api.Services;

public sealed class ProductService(IProductRepository repository, ILogger<ProductService> logger)
{
    public async Task<ProductResponse> CreateAsync(ProductRequest request, CancellationToken ct)
    {
        var product = await repository.CreateAsync(request, ct);
        logger.LogInformation("Created product {ProductId}", product.ProductId);
        return ProductResponse.From(product);
    }
    public async Task<ProductResponse> GetAsync(Guid id, CancellationToken ct) =>
        ProductResponse.From(await repository.GetAsync(id, ct) ?? throw Missing());
    public async Task<PagedResponse<ProductResponse>> ListAsync(PageQuery page, CancellationToken ct)
    {
        var (items, total) = await repository.ListAsync(page, ct);
        return new(items.Select(ProductResponse.From).ToArray(), page.PageNumber, page.PageSize, total);
    }
    public async Task<ProductResponse> UpdateAsync(Guid id, ProductRequest request, CancellationToken ct)
    {
        var product = await repository.UpdateAsync(id, request, ct) ?? throw Missing();
        logger.LogInformation("Updated product {ProductId}", id);
        return ProductResponse.From(product);
    }
    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        if (!await repository.DeleteAsync(id, ct)) throw Missing();
        logger.LogInformation("Soft-deleted product {ProductId}", id);
    }
    public async Task<IReadOnlyList<StockResponse>> ChangeStockAsync(StockBatchRequest request, bool restore, CancellationToken ct)
    {
        if (request.Items.Any(i => i is null)) throw new ApiException(400, "Stock items cannot be null.");
        List<(Guid Id, int Quantity)> items;
        try
        {
            items = request.Items.GroupBy(i => i.ProductId!.Value)
                .Select(g => (g.Key, g.Aggregate(0, (n, i) => checked(n + i.Quantity!.Value))))
                .ToList();
        }
        catch (OverflowException) { throw new ApiException(400, "Combined quantity exceeds the supported integer range."); }
        var result = await repository.ChangeStockAsync(items, restore, ct);
        logger.LogInformation("Stock batch {Operation} succeeded for {ProductCount} products", restore ? "restoration" : "deduction", items.Count);
        return result;
    }
    private static ApiException Missing() => new(404, "The active product was not found.");
}
