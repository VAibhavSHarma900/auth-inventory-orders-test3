using Order.Api.DTOs;
using Order.Api.Entities;
using Order.Api.Repositories;
using Test3.Common;
namespace Order.Api.Services;

public sealed class OrderService(IOrderRepository repository, OperationProcessor processor, ILogger<OrderService> logger)
{
    public async Task<OrderActionResult> CreateAsync(Guid userId, OrderRequest request, Guid requestKey, string correlationId, CancellationToken ct)
    {
        if (request.Items.Any(i => i is null)) throw new ApiException(400, "Order items cannot be null.");
        List<StockItem> items;
        try
        {
            items = request.Items.GroupBy(i => i.ProductId!.Value)
                .Select(g => new StockItem(g.Key, g.Aggregate(0, (n, i) => checked(n + i.Quantity!.Value))))
                .OrderBy(i => i.ProductId).ToList();
        }
        catch (OverflowException) { throw new ApiException(400, "Combined quantity exceeds the supported integer range."); }
        var operation = await repository.QueueCreateAsync(userId, items, requestKey, correlationId, ct);
        return await ProcessAsync(operation, ct);
    }
    public async Task<OrderActionResult> CancelAsync(Guid id, Guid userId, string correlationId, CancellationToken ct)
    {
        var current = await repository.GetAsync(id, userId, ct) ?? throw new ApiException(404, "The order was not found.");
        if (current.OrderStatus == "CANCELLED") return new(OrderResponse.From(current), false);
        var operation = await repository.QueueCancelAsync(id, userId, correlationId, ct);
        return await ProcessAsync(operation, ct);
    }
    private async Task<OrderActionResult> ProcessAsync(OrderOperation operation, CancellationToken ct)
    {
        try { await processor.ProcessAsync(operation.OperationId, ct); }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Operation {OperationId}, order {OrderId} requires recovery after {ExceptionType}", operation.OperationId, operation.OrderId, ex.GetType().Name);
        }
        var state = await repository.GetOperationAsync(operation.OperationId, ct);
        if (state.State == "REJECTED") throw new ApiException(state.ErrorStatus!.Value, state.ErrorDetail!);
        var order = await repository.GetAsync(operation.OrderId, operation.UserId, ct);
        if (order is null)
        {
            // A worker may reject and remove a provisional order between the two reads.
            state = await repository.GetOperationAsync(operation.OperationId, ct);
            if (state.State == "REJECTED") throw new ApiException(state.ErrorStatus!.Value, state.ErrorDetail!);
            throw new ApiException(404, "The order was not found.");
        }
        return new(OrderResponse.From(order), state.State == "PENDING");
    }
    public async Task<OrderResponse> GetAsync(Guid id, Guid? userId, CancellationToken ct) =>
        OrderResponse.From(await repository.GetAsync(id, userId, ct) ?? throw new ApiException(404, "The order was not found."));
    public async Task<IReadOnlyList<OrderResponse>> ListAsync(Guid? userId, CancellationToken ct) =>
        (await repository.ListAsync(userId, ct)).Select(OrderResponse.From).ToArray();
}
