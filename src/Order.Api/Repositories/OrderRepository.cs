using System.Data;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using Order.Api.DTOs;
using Order.Api.Entities;
using Test3.Common;
using OrderEntity = Order.Api.Entities.Order;
namespace Order.Api.Repositories;

public sealed class OrderRepository(NpgsqlDataSource source) : IOrderRepository
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal const string OperationColumns = "operation_id, order_id, user_id, operation_type, items, correlation_id, state, error_status, error_detail";

    public async Task<OrderOperation> QueueCreateAsync(Guid userId, IReadOnlyList<StockItem> items, Guid requestKey, string correlationId, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        // Serialize only duplicate client keys; unrelated orders remain concurrent.
        await using (var keyLock = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@key, 0))", connection, transaction))
        {
            keyLock.Parameters.AddWithValue("key", userId.ToString() + requestKey);
            await keyLock.ExecuteNonQueryAsync(ct);
        }
        await using (var find = new NpgsqlCommand($"SELECT {OperationColumns} FROM order_operations WHERE user_id = @user AND request_key = @key", connection, transaction))
        {
            find.Parameters.AddWithValue("user", userId);
            find.Parameters.AddWithValue("key", requestKey);
            await using var reader = await find.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                var previous = MapOperation(reader);
                if (!previous.Items.SequenceEqual(items)) throw new ApiException(409, "Idempotency-Key was already used with different items.");
                return previous;
            }
        }
        Guid orderId;
        await using (var insert = new NpgsqlCommand("INSERT INTO orders(user_id, order_status) VALUES (@user, 'CREATED') RETURNING order_id", connection, transaction))
        {
            insert.Parameters.AddWithValue("user", userId);
            orderId = (Guid)(await insert.ExecuteScalarAsync(ct))!;
        }
        foreach (var item in items)
        {
            await using var insert = new NpgsqlCommand("INSERT INTO order_items(order_id, product_id, quantity) VALUES (@order, @product, @quantity)", connection, transaction);
            insert.Parameters.AddWithValue("order", orderId);
            insert.Parameters.AddWithValue("product", item.ProductId);
            insert.Parameters.AddWithValue("quantity", item.Quantity);
            await insert.ExecuteNonQueryAsync(ct);
        }
        var operation = await InsertOperationAsync(connection, transaction, orderId, userId, "CREATE", requestKey, items, correlationId, ct);
        await transaction.CommitAsync(ct);
        return operation;
    }

    public async Task<OrderOperation> QueueCancelAsync(Guid orderId, Guid userId, string correlationId, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var order = await ReadAsync(connection, transaction, orderId, userId, true, ct) ?? throw new ApiException(404, "The order was not found.");
        await using (var find = new NpgsqlCommand($"SELECT {OperationColumns} FROM order_operations WHERE order_id = @order AND operation_type = 'CANCEL'", connection, transaction))
        {
            find.Parameters.AddWithValue("order", orderId);
            await using var reader = await find.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct)) return MapOperation(reader);
        }
        if (order.OrderStatus != "CONFIRMED") throw new ApiException(409, "Only confirmed orders can be cancelled.");
        var items = order.Items.Select(i => new StockItem(i.ProductId, i.Quantity)).OrderBy(i => i.ProductId).ToArray();
        var operation = await InsertOperationAsync(connection, transaction, orderId, userId, "CANCEL", null, items, correlationId, ct);
        await transaction.CommitAsync(ct);
        return operation;
    }

    private static async Task<OrderOperation> InsertOperationAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid orderId, Guid userId, string type, Guid? key, IReadOnlyList<StockItem> items, string correlationId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand($"""
            INSERT INTO order_operations(operation_id, order_id, user_id, operation_type, request_key, items, correlation_id)
            VALUES (gen_random_uuid(), @order, @user, @type, @key, @items, @correlation) RETURNING {OperationColumns}
            """, connection, transaction);
        cmd.Parameters.AddWithValue("order", orderId);
        cmd.Parameters.AddWithValue("user", userId);
        cmd.Parameters.AddWithValue("type", type);
        cmd.Parameters.Add("key", NpgsqlDbType.Uuid).Value = key is null ? DBNull.Value : key.Value;
        cmd.Parameters.AddWithValue("items", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(items, Json));
        cmd.Parameters.AddWithValue("correlation", correlationId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return MapOperation(reader);
    }

    public async Task<OrderOperation> GetOperationAsync(Guid operationId, CancellationToken ct)
    {
        await using var cmd = source.CreateCommand($"SELECT {OperationColumns} FROM order_operations WHERE operation_id = @id");
        cmd.Parameters.AddWithValue("id", operationId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new ApiException(404, "The operation was not found.");
        return MapOperation(reader);
    }

    public async Task<IReadOnlyList<Guid>> PendingAsync(CancellationToken ct)
    {
        await using var cmd = source.CreateCommand("SELECT operation_id FROM order_operations WHERE state = 'PENDING' AND next_attempt_at <= CURRENT_TIMESTAMP ORDER BY next_attempt_at LIMIT 10");
        var ids = new List<Guid>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) ids.Add(reader.GetGuid(0));
        return ids;
    }

    public async Task<IOperationWrite?> BeginOperationAsync(Guid operationId, CancellationToken ct)
    {
        var connection = await source.OpenConnectionAsync(ct);
        NpgsqlTransaction? transaction = null;
        try
        {
            transaction = await connection.BeginTransactionAsync(ct);
            await using var cmd = new NpgsqlCommand($"SELECT {OperationColumns} FROM order_operations WHERE operation_id = @id AND state = 'PENDING' AND next_attempt_at <= CURRENT_TIMESTAMP FOR UPDATE SKIP LOCKED", connection, transaction);
            cmd.Parameters.AddWithValue("id", operationId);
            OrderOperation? operation;
            await using (var reader = await cmd.ExecuteReaderAsync(ct))
                operation = await reader.ReadAsync(ct) ? MapOperation(reader) : null;
            if (operation is not null) return new OperationWrite(connection, transaction, operation);
            await transaction.DisposeAsync();
            await connection.DisposeAsync();
            return null;
        }
        catch
        {
            if (transaction is not null) await transaction.DisposeAsync();
            await connection.DisposeAsync();
            throw;
        }
    }

    internal static OrderOperation MapOperation(NpgsqlDataReader reader) => new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3),
        JsonSerializer.Deserialize<StockItem[]>(reader.GetString(4), Json)!, reader.GetString(5), reader.GetString(6),
        reader.IsDBNull(7) ? null : reader.GetInt32(7), reader.IsDBNull(8) ? null : reader.GetString(8));

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
                   i.order_item_id, i.product_id, i.quantity, p.operation_type
            FROM orders o LEFT JOIN order_items i ON i.order_id = o.order_id
            LEFT JOIN order_operations p ON p.order_id = o.order_id AND p.state = 'PENDING'
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
                orders.Add(new(id, reader.GetGuid(1), reader.GetString(2), reader.GetDateTime(3), items, reader.IsDBNull(7) ? null : reader.GetString(7)));
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
        string? pending;
        // Only a fixed SQL fragment is interpolated. All request values remain parameters.
        await using (var cmd = new NpgsqlCommand("SELECT user_id, order_status, created_at, (SELECT operation_type FROM order_operations WHERE order_id = orders.order_id AND state = 'PENDING') FROM orders WHERE order_id = @id AND (@all OR user_id = @user)" + (forUpdate ? " FOR UPDATE" : ""), connection, transaction))
        {
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("all", userId is null);
            cmd.Parameters.AddWithValue("user", userId ?? Guid.Empty);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) return null;
            owner = r.GetGuid(0); status = r.GetString(1); created = r.GetDateTime(2); pending = r.IsDBNull(3) ? null : r.GetString(3);
        }
        var items = new List<OrderItem>();
        await using (var cmd = new NpgsqlCommand("SELECT order_item_id, product_id, quantity FROM order_items WHERE order_id = @id ORDER BY product_id", connection, transaction))
        {
            cmd.Parameters.AddWithValue("id", id);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) items.Add(new(r.GetGuid(0), r.GetGuid(1), r.GetInt32(2)));
        }
        return new(id, owner, status, created, items, pending);
    }
}

internal sealed class OperationWrite(NpgsqlConnection connection, NpgsqlTransaction transaction, OrderOperation operation) : IOperationWrite
{
    public OrderOperation Operation => operation;
    public async Task FinishAsync(StockOperationResult result, CancellationToken ct)
    {
        var applied = result.Outcome == "APPLIED";
        var sql = applied ? "UPDATE orders SET order_status = @status WHERE order_id = @id"
            : operation.OperationType == "CREATE" ? "DELETE FROM orders WHERE order_id = @id" : null;
        if (sql is not null)
        {
            await using var order = new NpgsqlCommand(sql, connection, transaction);
            order.Parameters.AddWithValue("id", operation.OrderId);
            if (applied) order.Parameters.AddWithValue("status", operation.OperationType == "CREATE" ? "CONFIRMED" : "CANCELLED");
            await order.ExecuteNonQueryAsync(ct);
        }
        await using var cmd = new NpgsqlCommand("""
            UPDATE order_operations SET state = @state, error_status = @status, error_detail = @detail, attempts = attempts + 1
            WHERE operation_id = @id
            """, connection, transaction);
        cmd.Parameters.AddWithValue("id", operation.OperationId);
        cmd.Parameters.AddWithValue("state", applied ? "COMPLETED" : "REJECTED");
        cmd.Parameters.Add("status", NpgsqlDbType.Integer).Value = result.ErrorStatus is null ? DBNull.Value : result.ErrorStatus.Value;
        cmd.Parameters.Add("detail", NpgsqlDbType.Text).Value = (object?)result.ErrorDetail ?? DBNull.Value;
        await cmd.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
    }
    public async Task RetryAsync(CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("UPDATE order_operations SET attempts = attempts + 1, next_attempt_at = clock_timestamp() + INTERVAL '5 seconds' WHERE operation_id = @id", connection, transaction);
        cmd.Parameters.AddWithValue("id", operation.OperationId);
        await cmd.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
    }
    public async ValueTask DisposeAsync()
    {
        try { await transaction.DisposeAsync(); }
        finally { await connection.DisposeAsync(); }
    }
}
