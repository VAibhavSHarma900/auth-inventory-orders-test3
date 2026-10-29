using Auth.Api.Entities;

namespace Auth.Api.Repositories;

public interface IUserRepository
{
    Task<User> CreateAsync(string username, string hash, CancellationToken ct);
    Task<User?> FindByNameAsync(string username, CancellationToken ct);
    Task<User?> GetAsync(Guid id, CancellationToken ct);
    Task UpdateHashAsync(Guid id, string oldHash, string hash, CancellationToken ct);
}
