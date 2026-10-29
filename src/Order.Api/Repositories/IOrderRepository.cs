using Order.Api.DTOs;
using Order.Api.Entities;
using OrderEntity = Order.Api.Entities.Order;
namespace Order.Api.Repositories;

public interface IOrderRepository
{
    Task<OrderOperation> QueueCreateAsync(Guid userId, IReadOnlyList<StockItem> items, Guid requestKey, string correlationId, CancellationToken ct);
    Task<OrderOperation> QueueCancelAsync(Guid orderId, Guid userId, string correlationId, CancellationToken ct);
    Task<OrderOperation> GetOperationAsync(Guid operationId, CancellationToken ct);
    Task<IReadOnlyList<Guid>> PendingAsync(CancellationToken ct);
    Task<IOperationWrite?> BeginOperationAsync(Guid operationId, CancellationToken ct);
    Task<OrderEntity?> GetAsync(Guid id, Guid? userId, CancellationToken ct);
    Task<IReadOnlyList<OrderEntity>> ListAsync(Guid? userId, CancellationToken ct);
}
public interface IOperationWrite : IAsyncDisposable
{
    OrderOperation Operation { get; }
    Task FinishAsync(StockOperationResult result, CancellationToken ct);
    Task RetryAsync(CancellationToken ct);
}
