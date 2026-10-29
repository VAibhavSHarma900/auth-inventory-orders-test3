using Inventory.Api.DTOs;
using Inventory.Api.Entities;

namespace Inventory.Api.Repositories;

public interface IProductRepository
{
    Task<Product> CreateAsync(ProductRequest request, CancellationToken ct);
    Task<Product?> GetAsync(Guid id, CancellationToken ct);
    Task<(IReadOnlyList<Product> Items, long Total)> ListAsync(PageQuery page, CancellationToken ct);
    Task<Product?> UpdateAsync(Guid id, ProductRequest request, CancellationToken ct);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<StockResponse>> ChangeStockAsync(IReadOnlyList<(Guid Id, int Quantity)> items, bool restore, CancellationToken ct);
}
