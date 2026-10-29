using Order.Api.DTOs;
using OrderEntity = Order.Api.Entities.Order;

namespace Order.Api.Repositories;

public interface IOrderRepository
{
    Task<IOrderWrite> BeginCreateAsync(Guid userId, IReadOnlyList<StockItem> items, CancellationToken ct);
    Task<IOrderWrite> BeginCancelAsync(Guid id, Guid userId, CancellationToken ct);
    Task<OrderEntity?> GetAsync(Guid id, Guid? userId, CancellationToken ct);
    Task<IReadOnlyList<OrderEntity>> ListAsync(Guid? userId, CancellationToken ct);
}

public interface IOrderWrite : IAsyncDisposable
{
    OrderEntity Order { get; }
    Task SetStatusAndCommitAsync(string status, CancellationToken ct);
}
