using Order.Api.DTOs;

namespace Order.Api.Services;

public interface IInventoryClient
{
    Task<ProductSnapshot> GetAsync(Guid id, string token, string correlationId, CancellationToken ct);
    Task ChangeAsync(IReadOnlyList<StockItem> items, bool restore, string token, string correlationId, CancellationToken ct);
}
