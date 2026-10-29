using System.Security.Claims;
using Order.Api.DTOs;
using Order.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Order.Api.Controllers;

[ApiController, Authorize(Roles = "ADMIN,USER"), Route("api/orders")]
public sealed class OrdersController(OrderService service) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue("sub")!);
    private string Token => Request.Headers.Authorization.ToString()["Bearer ".Length..].Trim();
    [HttpPost, Authorize(Roles = "USER")]
    public async Task<ActionResult<OrderResponse>> Create(OrderRequest request, CancellationToken ct)
    {
        var order = await service.CreateAsync(UserId, request, Token, HttpContext.TraceIdentifier, ct);
        return CreatedAtAction(nameof(Get), new { id = order.OrderId }, order);
    }
    [HttpGet("my-orders")]
    public Task<IReadOnlyList<OrderResponse>> List(CancellationToken ct) => service.ListAsync(User.IsInRole("ADMIN") ? null : UserId, ct);
    [HttpGet("{id:guid}")]
    public Task<OrderResponse> Get(Guid id, CancellationToken ct) => service.GetAsync(id, User.IsInRole("ADMIN") ? null : UserId, ct);
    [HttpPatch("{id:guid}/cancel"), Authorize(Roles = "USER")]
    public Task<OrderResponse> Cancel(Guid id, CancellationToken ct) => service.CancelAsync(id, UserId, Token, HttpContext.TraceIdentifier, ct);
}
