using System.Data;
using Npgsql;
using Order.Api.DTOs;
using Order.Api.Entities;
using Test3.Common;
using OrderEntity = Order.Api.Entities.Order;
namespace Order.Api.Repositories;

public sealed class OrderRepository(NpgsqlDataSource source) : IOrderRepository
{
    public async Task<IOrderWrite> BeginCreateAsync(Guid userId, IReadOnlyList<StockItem> items, CancellationToken ct)
    {
        var connection = await source.OpenConnectionAsync(ct);
        NpgsqlTransaction? transaction = null;
        try
        {
            transaction = await connection.BeginTransactionAsync(ct);
            Guid orderId;
            DateTime createdAt;
            await using (var cmd = new NpgsqlCommand("INSERT INTO orders (user_id, order_status) VALUES (@user, 'CREATED') RETURNING order_id, created_at", connection, transaction))
            {
                cmd.Parameters.AddWithValue("user", userId);
                await using var r = await cmd.ExecuteReaderAsync(ct);
                await r.ReadAsync(ct);
                orderId = r.GetGuid(0);
                createdAt = r.GetDateTime(1);
            }
            var stored = new List<OrderItem>();
            foreach (var item in items)
            {
                await using var cmd = new NpgsqlCommand("INSERT INTO order_items (order_id, product_id, quantity) VALUES (@order, @product, @quantity) RETURNING order_item_id", connection, transaction);
                cmd.Parameters.AddWithValue("order", orderId);
                cmd.Parameters.AddWithValue("product", item.ProductId);
                cmd.Parameters.AddWithValue("quantity", item.Quantity);
                stored.Add(new((Guid)(await cmd.ExecuteScalarAsync(ct))!, item.ProductId, item.Quantity));
            }
            return new OrderWrite(connection, transaction, new(orderId, userId, "CREATED", createdAt, stored));
        }
        catch
        {
            if (transaction is not null) await transaction.DisposeAsync();
            await connection.DisposeAsync();
            throw;
        }
    }

    public async Task<IOrderWrite> BeginCancelAsync(Guid id, Guid userId, CancellationToken ct)
    {
        var connection = await source.OpenConnectionAsync(ct);
        NpgsqlTransaction? transaction = null;
        try
        {
            transaction = await connection.BeginTransactionAsync(ct);
            var order = await ReadAsync(connection, transaction, id, userId, true, ct)
                ?? throw new ApiException(404, "The order was not found.");
            return new OrderWrite(connection, transaction, order);
        }
        catch
        {
            if (transaction is not null) await transaction.DisposeAsync();
            await connection.DisposeAsync();
            throw;
        }
    }

    public async Task<OrderEntity?> GetAsync(Guid id, Guid? userId, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var order = await ReadAsync(connection, transaction, id, userId, false, ct);
        await transaction.CommitAsync(ct);
        return order;
    }

    public async Task<IReadOnlyList<OrderEntity>> ListAsync(Guid? userId, CancellationToken ct)
    {
        await using var cmd = source.CreateCommand("""
            SELECT o.order_id, o.user_id, o.order_status, o.created_at,
                   i.order_item_id, i.product_id, i.quantity
            FROM orders o LEFT JOIN order_items i ON i.order_id = o.order_id
            WHERE (@all OR o.user_id = @user)
            ORDER BY o.created_at DESC, o.order_id DESC, i.product_id
            """);
        cmd.Parameters.AddWithValue("all", userId is null);
        cmd.Parameters.AddWithValue("user", userId ?? Guid.Empty);
        var orders = new List<OrderEntity>();
        var items = new List<OrderItem>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var id = reader.GetGuid(0);
            if (orders.Count == 0 || orders[^1].OrderId != id)
            {
                items = [];
                orders.Add(new(id, reader.GetGuid(1), reader.GetString(2), reader.GetDateTime(3), items));
            }
            if (!reader.IsDBNull(4)) items.Add(new(reader.GetGuid(4), reader.GetGuid(5), reader.GetInt32(6)));
        }
        return orders;
    }

    private static async Task<OrderEntity?> ReadAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid id, Guid? userId, bool forUpdate, CancellationToken ct)
    {
        Guid owner;
        string status;
        DateTime created;
        // Only a fixed SQL fragment is interpolated. All request values remain parameters.
        await using (var cmd = new NpgsqlCommand("SELECT user_id, order_status, created_at FROM orders WHERE order_id = @id AND (@all OR user_id = @user)" + (forUpdate ? " FOR UPDATE" : ""), connection, transaction))
        {
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("all", userId is null);
            cmd.Parameters.AddWithValue("user", userId ?? Guid.Empty);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) return null;
            owner = r.GetGuid(0); status = r.GetString(1); created = r.GetDateTime(2);
        }
        var items = new List<OrderItem>();
        await using (var cmd = new NpgsqlCommand("SELECT order_item_id, product_id, quantity FROM order_items WHERE order_id = @id ORDER BY product_id", connection, transaction))
        {
            cmd.Parameters.AddWithValue("id", id);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) items.Add(new(r.GetGuid(0), r.GetGuid(1), r.GetInt32(2)));
        }
        return new(id, owner, status, created, items);
    }
}

// Disposal rolls back uncommitted work and releases the order lock.
public sealed class OrderWrite(NpgsqlConnection connection, NpgsqlTransaction transaction, OrderEntity order) : IOrderWrite
{
    public OrderEntity Order { get; private set; } = order;
    public async Task SetStatusAndCommitAsync(string status, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("UPDATE orders SET order_status = @status WHERE order_id = @id", connection, transaction);
        cmd.Parameters.AddWithValue("id", Order.OrderId);
        cmd.Parameters.AddWithValue("status", status);
        await cmd.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
        Order = Order with { OrderStatus = status };
    }
    public async ValueTask DisposeAsync()
    {
        try { await transaction.DisposeAsync(); }
        finally { await connection.DisposeAsync(); }
    }
}
