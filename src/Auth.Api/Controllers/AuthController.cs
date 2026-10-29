using System.Security.Claims;
using Auth.Api.DTOs;
using Auth.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Auth.Api.Controllers;

[ApiController, Route("api/auth")]
public sealed class AuthController(AuthService service) : ControllerBase
{
    [AllowAnonymous, HttpPost("register")]
    public async Task<ActionResult<UserResponse>> Register(CredentialsRequest request, CancellationToken ct)
    {
        var user = await service.RegisterAsync(request, ct);
        return Created("/api/auth/me", user);
    }
    [AllowAnonymous, HttpPost("login")]
    public Task<LoginResponse> Login(CredentialsRequest request, CancellationToken ct) => service.LoginAsync(request, ct);
    [Authorize, HttpGet("me")]
    public Task<UserResponse> Me(CancellationToken ct) => service.MeAsync(Guid.Parse(User.FindFirstValue("sub")!), ct);
}
