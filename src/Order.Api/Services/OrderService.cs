using Order.Api.DTOs;
using Order.Api.Repositories;
using Test3.Common;
namespace Order.Api.Services;

public sealed class OrderService(IOrderRepository repository, IInventoryClient inventory, ILogger<OrderService> logger)
{
    public async Task<OrderResponse> CreateAsync(Guid userId, OrderRequest request, string token, string correlationId, CancellationToken ct)
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
        foreach (var item in items)
        {
            var product = await inventory.GetAsync(item.ProductId, token, correlationId, ct);
            if (product.StockQty < item.Quantity) throw new ApiException(409, "Insufficient stock for the requested products.");
        }
        await using var write = await repository.BeginCreateAsync(userId, items, ct);
        await MutateAndCommitAsync(write, items, false, token, correlationId, ct);
        return OrderResponse.From(write.Order);
    }
    public async Task<OrderResponse> CancelAsync(Guid id, Guid userId, string token, string correlationId, CancellationToken ct)
    {
        await using var write = await repository.BeginCancelAsync(id, userId, ct);
        if (write.Order.OrderStatus == "CANCELLED") return OrderResponse.From(write.Order);
        if (write.Order.OrderStatus != "CONFIRMED") throw new ApiException(409, "Only confirmed orders can be cancelled.");
        var items = write.Order.Items.Select(i => new StockItem(i.ProductId, i.Quantity)).ToArray();
        await MutateAndCommitAsync(write, items, true, token, correlationId, ct);
        return OrderResponse.From(write.Order);
    }
    public async Task<OrderResponse> GetAsync(Guid id, Guid? userId, CancellationToken ct) =>
        OrderResponse.From(await repository.GetAsync(id, userId, ct) ?? throw new ApiException(404, "The order was not found."));
    public async Task<IReadOnlyList<OrderResponse>> ListAsync(Guid? userId, CancellationToken ct) =>
        (await repository.ListAsync(userId, ct)).Select(OrderResponse.From).ToArray();

    private async Task MutateAndCommitAsync(IOrderWrite write, IReadOnlyList<StockItem> items, bool restore, string token, string correlationId, CancellationToken ct)
    {
        var operation = restore ? "cancellation" : "creation";
        var stockConfirmed = false;
        logger.LogInformation("Starting order {Operation} for {OrderId}, user {UserId}, items {@Items}", operation, write.Order.OrderId, write.Order.UserId, items);
        try
        {
            await inventory.ChangeAsync(items, restore, token, correlationId, ct);
            stockConfirmed = true;
            await write.SetStatusAndCommitAsync(restore ? "CANCELLED" : "CONFIRMED", ct);
            logger.LogInformation("Order {OrderId} {Operation} succeeded", write.Order.OrderId, operation);
        }
        catch (ApiException ex) when (!stockConfirmed && ex.StatusCode is 404 or 409)
        {
            logger.LogWarning("Order {OrderId} {Operation} rejected: {StatusCode}", write.Order.OrderId, operation, ex.StatusCode);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError("UNCERTAIN order {OrderId}, operation {Operation}, stock response confirmed {StockConfirmed}, exception {ExceptionType}; reconcile before retrying",
                write.Order.OrderId, operation, stockConfirmed, ex.GetType().Name);
            throw;
        }
    }
}
