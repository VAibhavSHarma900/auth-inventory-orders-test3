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
        await transaction.CommitAsync(ct);
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
