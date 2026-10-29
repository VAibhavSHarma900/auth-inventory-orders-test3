using System.ComponentModel.DataAnnotations;
using Auth.Api.Entities;
namespace Auth.Api.DTOs;

public sealed class CredentialsRequest
{
    [Required, StringLength(100)]
    [RegularExpression(@"[^\u0000]*", ErrorMessage = "Username cannot contain null characters.")]
    public string Username { get; init; } = "";
    [Required] public string Password { get; init; } = "";
}
public sealed record UserResponse(Guid UserId, string Username, string Role, bool IsActive, DateTime CreatedAt)
{
    public static UserResponse From(User user) => new(user.UserId, user.Username, user.Role, user.IsActive, user.CreatedAt);
}
public sealed record LoginResponse(string AccessToken, string TokenType, DateTime ExpiresAt, UserResponse User);
