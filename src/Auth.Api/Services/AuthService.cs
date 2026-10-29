using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Auth.Api.DTOs;
using Auth.Api.Entities;
using Auth.Api.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using Test3.Common;
namespace Auth.Api.Services;

public sealed class AuthService(IUserRepository users, IPasswordHasher<User> hasher, IConfiguration config, ILogger<AuthService> logger)
{
    public async Task<UserResponse> RegisterAsync(CredentialsRequest request, CancellationToken ct)
    {
        var username = request.Username.Trim();
        var pending = new User(Guid.Empty, username, "", "USER", true, default);
        var user = await users.CreateAsync(username, hasher.HashPassword(pending, request.Password), ct);
        logger.LogInformation("Registered user {UserId}", user.UserId);
        return UserResponse.From(user);
    }
    public async Task<LoginResponse> LoginAsync(CredentialsRequest request, CancellationToken ct)
    {
        var user = await users.FindByNameAsync(request.Username.Trim(), ct);
        if (user is null || !user.IsActive)
        {
            logger.LogWarning("Login rejected");
            throw InvalidCredentials();
        }
        var result = hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (result == PasswordVerificationResult.Failed)
        {
            logger.LogWarning("Login rejected for user {UserId}", user.UserId);
            throw InvalidCredentials();
        }
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
            await users.UpdateHashAsync(user.UserId, user.PasswordHash, hasher.HashPassword(user, request.Password), ct);
        var expires = DateTime.UtcNow.AddHours(1);
        var token = new JwtSecurityToken(config["Jwt:Issuer"], config["Jwt:Audience"],
            [new Claim("sub", user.UserId.ToString()), new Claim("role", user.Role), new Claim("jti", Guid.NewGuid().ToString())],
            notBefore: DateTime.UtcNow, expires: expires,
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!)), SecurityAlgorithms.HmacSha256));
        logger.LogInformation("Login succeeded for user {UserId}", user.UserId);
        return new(new JwtSecurityTokenHandler().WriteToken(token), "Bearer", expires, UserResponse.From(user));
    }
    public async Task<UserResponse> MeAsync(Guid id, CancellationToken ct) =>
        UserResponse.From(await users.GetAsync(id, ct) ?? throw new ApiException(401, "The account is unavailable."));
    private static ApiException InvalidCredentials() => new(401, "Invalid username or password.");
}
