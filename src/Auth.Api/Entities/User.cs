namespace Auth.Api.Entities;
public sealed record User(Guid UserId, string Username, string PasswordHash, string Role, bool IsActive, DateTime CreatedAt);
