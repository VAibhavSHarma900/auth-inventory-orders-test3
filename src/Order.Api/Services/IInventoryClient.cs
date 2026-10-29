using Order.Api.DTOs;
using Order.Api.Entities;
namespace Order.Api.Services;
public interface IInventoryClient
{
    Task<StockOperationResult> ExecuteAsync(OrderOperation operation, CancellationToken ct);
}
