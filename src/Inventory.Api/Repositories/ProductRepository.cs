using System.Data;
using Test3.Common;
using Npgsql;
using Inventory.Api.DTOs;
using ProductEntity = Inventory.Api.Entities.Product;

namespace Inventory.Api.Repositories;

public sealed class ProductRepository(NpgsqlDataSource source) : IProductRepository
{
    private const string Columns = "product_id, product_name, stock_qty, is_active, created_at, updated_at";

    public async Task<ProductEntity> CreateAsync(ProductRequest request, CancellationToken ct)
    {
        await using var cmd = source.CreateCommand($"""
            INSERT INTO products (product_name, stock_qty) VALUES (@name, @stock)
            RETURNING {Columns}
            """);
        AddValues(cmd, request);
        return (await ReadAsync(cmd, ct))!;
    }

    public async Task<ProductEntity?> GetAsync(Guid id, CancellationToken ct)
    {
        await using var cmd = source.CreateCommand($"SELECT {Columns} FROM products WHERE product_id = @id AND is_active");
        cmd.Parameters.AddWithValue("id", id);
        return await ReadAsync(cmd, ct);
    }

    public async Task<(IReadOnlyList<ProductEntity> Items, long Total)> ListAsync(PageQuery page, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await using var count = new NpgsqlCommand("SELECT COUNT(*) FROM products WHERE is_active", connection, transaction);
        var total = (long)(await count.ExecuteScalarAsync(ct))!;
        await using var cmd = new NpgsqlCommand($"""
            SELECT {Columns} FROM products WHERE is_active ORDER BY created_at DESC, product_id DESC
            LIMIT @limit OFFSET @offset
            """, connection, transaction);
        cmd.Parameters.AddWithValue("limit", page.PageSize);
        cmd.Parameters.AddWithValue("offset", ((long)page.PageNumber - 1) * page.PageSize);
        var items = new List<ProductEntity>();
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) items.Add(Map(reader));
        await transaction.CommitAsync(ct);
        return (items, total);
    }

    public async Task<ProductEntity?> UpdateAsync(Guid id, ProductRequest request, CancellationToken ct)
    {
        await using var cmd = source.CreateCommand($"""
            UPDATE products SET product_name = @name, stock_qty = @stock, updated_at = CURRENT_TIMESTAMP
            WHERE product_id = @id AND is_active RETURNING {Columns}
            """);
        cmd.Parameters.AddWithValue("id", id);
        AddValues(cmd, request);
        return await ReadAsync(cmd, ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        await using var cmd = source.CreateCommand("""
            UPDATE products SET is_active = FALSE, updated_at = CURRENT_TIMESTAMP
            WHERE product_id = @id AND is_active
            """);
        cmd.Parameters.AddWithValue("id", id);
        return await cmd.ExecuteNonQueryAsync(ct) == 1;
    }


    public async Task<IReadOnlyList<StockResponse>> ChangeStockAsync(IReadOnlyList<(Guid Id, int Quantity)> items, bool restore, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var result = await ApplyStockAsync(connection, transaction, items, restore, ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    public async Task<StockOperationResult> ExecuteOperationAsync(StockOperationRequest request, Guid userId, string hash,
        IReadOnlyList<(Guid Id, int Quantity)> items, bool restore, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var insert = new NpgsqlCommand("""
            INSERT INTO stock_operations(operation_id, order_id, user_id, operation_type, request_hash, outcome)
            VALUES (@operation, @order, @user, @type, @hash, 'APPLIED')
            ON CONFLICT DO NOTHING
            """, connection, transaction);
        insert.Parameters.AddWithValue("operation", request.OperationId!.Value);
        insert.Parameters.AddWithValue("order", request.OrderId!.Value);
        insert.Parameters.AddWithValue("user", userId);
        insert.Parameters.AddWithValue("type", restore ? "RESTORE" : "DEDUCT");
        insert.Parameters.AddWithValue("hash", hash);
        var firstAttempt = await insert.ExecuteNonQueryAsync(ct) == 1;
        if (firstAttempt)
        {
            // The ledger and stock share one transaction; neither can commit without the other.
            try { await ApplyStockAsync(connection, transaction, items, restore, ct); }
            catch (ApiException ex) when (ex.StatusCode is 404 or 409)
            {
                await using var reject = new NpgsqlCommand("""
                    UPDATE stock_operations SET outcome = 'REJECTED', error_status = @status, error_detail = @detail
                    WHERE operation_id = @operation
                    """, connection, transaction);
                reject.Parameters.AddWithValue("operation", request.OperationId.Value);
                reject.Parameters.AddWithValue("status", ex.StatusCode);
                reject.Parameters.AddWithValue("detail", ex.Message);
                await reject.ExecuteNonQueryAsync(ct);
            }
        }
        StockOperationResult result;
        await using (var read = new NpgsqlCommand("SELECT request_hash, outcome, error_status, error_detail FROM stock_operations WHERE operation_id = @operation", connection, transaction))
        {
            read.Parameters.AddWithValue("operation", request.OperationId.Value);
            await using var reader = await read.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) throw new ApiException(409, "The order already has a different operation of this type.");
            if (reader.GetString(0) != hash) throw new ApiException(409, "Operation ID was already used with different content.");
            result = new(request.OperationId.Value, reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetInt32(2), reader.IsDBNull(3) ? null : reader.GetString(3));
        }
        await transaction.CommitAsync(ct);
        return result;
    }

    private static async Task<IReadOnlyList<StockResponse>> ApplyStockAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        IReadOnlyList<(Guid Id, int Quantity)> items, bool restore, CancellationToken ct)
    {
        var locked = new List<(Guid Id, int Quantity, int Stock)>();
        // Every mutation locks rows in the same order, preventing batch-to-batch deadlocks.
        foreach (var item in items.OrderBy(i => i.Id))
        {
            await using var cmd = new NpgsqlCommand("SELECT stock_qty, is_active FROM products WHERE product_id = @id FOR UPDATE", connection, transaction);
            cmd.Parameters.AddWithValue("id", item.Id);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct) || (!restore && !reader.GetBoolean(1)))
                throw new ApiException(404, "A requested product is missing or inactive.");
            var stock = reader.GetInt32(0);
            if (!restore && stock < item.Quantity) throw new ApiException(409, "Insufficient stock for the requested products.");
            if (restore && (long)stock + item.Quantity > int.MaxValue) throw new ApiException(409, "Restoring stock would exceed the supported quantity.");
            locked.Add((item.Id, item.Quantity, stock));
        }
        var result = new List<StockResponse>();
        foreach (var item in locked)
        {
            var stock = restore ? item.Stock + item.Quantity : item.Stock - item.Quantity;
            await using var cmd = new NpgsqlCommand("UPDATE products SET stock_qty = @stock, updated_at = CURRENT_TIMESTAMP WHERE product_id = @id", connection, transaction);
            cmd.Parameters.AddWithValue("id", item.Id);
            cmd.Parameters.AddWithValue("stock", stock);
            await cmd.ExecuteNonQueryAsync(ct);
            result.Add(new(item.Id, stock));
        }
        return result;
    }

    private static void AddValues(NpgsqlCommand cmd, ProductRequest r)
    {
        cmd.Parameters.AddWithValue("name", r.Name.Trim());
        cmd.Parameters.AddWithValue("stock", r.StockQty!.Value);
    }

    private static async Task<ProductEntity?> ReadAsync(NpgsqlCommand cmd, CancellationToken ct)
    {
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    private static ProductEntity Map(NpgsqlDataReader r) => new(r.GetGuid(0), r.GetString(1),
        r.GetInt32(2), r.GetBoolean(3), r.GetDateTime(4), r.IsDBNull(5) ? null : r.GetDateTime(5));
}
