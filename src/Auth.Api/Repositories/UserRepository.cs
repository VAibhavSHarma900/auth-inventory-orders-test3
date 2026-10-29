using Auth.Api.Entities;
using Npgsql;
using Test3.Common;
namespace Auth.Api.Repositories;

public sealed class UserRepository(NpgsqlDataSource source) : IUserRepository
{
    private const string Columns = "user_id, username, password_hash, role, is_active, created_at";
    public async Task<User> CreateAsync(string username, string hash, CancellationToken ct)
    {
        await using var cmd = source.CreateCommand($"INSERT INTO users (username, password_hash, role) VALUES (@name, @hash, 'USER') RETURNING {Columns}");
        cmd.Parameters.AddWithValue("name", username);
        cmd.Parameters.AddWithValue("hash", hash);
        try { return (await ReadAsync(cmd, ct))!; }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        { throw new ApiException(409, "This username is already registered."); }
    }
    public async Task<User?> FindByNameAsync(string username, CancellationToken ct)
    {
        await using var cmd = source.CreateCommand($"SELECT {Columns} FROM users WHERE username = @name");
        cmd.Parameters.AddWithValue("name", username);
        return await ReadAsync(cmd, ct);
    }
    public async Task<User?> GetAsync(Guid id, CancellationToken ct)
    {
        await using var cmd = source.CreateCommand($"SELECT {Columns} FROM users WHERE user_id = @id AND is_active");
        cmd.Parameters.AddWithValue("id", id);
        return await ReadAsync(cmd, ct);
    }
    public async Task UpdateHashAsync(Guid id, string oldHash, string hash, CancellationToken ct)
    {
        await using var cmd = source.CreateCommand("UPDATE users SET password_hash = @hash WHERE user_id = @id AND password_hash = @oldHash");
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("oldHash", oldHash);
        cmd.Parameters.AddWithValue("hash", hash);
        await cmd.ExecuteNonQueryAsync(ct);
    }
    private static async Task<User?> ReadAsync(NpgsqlCommand cmd, CancellationToken ct)
    {
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? new(r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetBoolean(4), r.GetDateTime(5)) : null;
    }
}
