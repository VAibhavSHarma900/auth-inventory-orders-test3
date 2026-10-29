using System.Security.Claims;
using Order.Api.DTOs;
using Order.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Test3.Common;
namespace Order.Api.Controllers;

[ApiController, Authorize(Roles = "ADMIN,USER"), Route("api/orders")]
public sealed class OrdersController(OrderService service) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue("sub")!);
    [HttpPost, Authorize(Roles = "USER")]
    public async Task<ActionResult<OrderResponse>> Create(OrderRequest request, CancellationToken ct)
    {
        var supplied = Request.Headers["Idempotency-Key"].ToString();
        var key = Guid.NewGuid();
        if (Request.Headers.ContainsKey("Idempotency-Key") && (!Guid.TryParse(supplied, out key) || key == Guid.Empty))
            throw new ApiException(400, "Idempotency-Key must be a nonempty UUID.");
        Response.Headers["Idempotency-Key"] = key.ToString();
        var result = await service.CreateAsync(UserId, request, key, HttpContext.TraceIdentifier, ct);
        if (result.IsPending) return Pending(result.Order);
        return CreatedAtAction(nameof(Get), new { id = result.Order.OrderId }, result.Order);
    }
    [HttpGet("my-orders")]
    public Task<IReadOnlyList<OrderResponse>> List(CancellationToken ct) => service.ListAsync(User.IsInRole("ADMIN") ? null : UserId, ct);
    [HttpGet("{id:guid}")]
    public Task<OrderResponse> Get(Guid id, CancellationToken ct) => service.GetAsync(id, User.IsInRole("ADMIN") ? null : UserId, ct);
    [HttpPatch("{id:guid}/cancel"), Authorize(Roles = "USER")]
    public async Task<ActionResult<OrderResponse>> Cancel(Guid id, CancellationToken ct)
    {
        var result = await service.CancelAsync(id, UserId, HttpContext.TraceIdentifier, ct);
        return result.IsPending ? Pending(result.Order) : Ok(result.Order);
    }
    private ActionResult<OrderResponse> Pending(OrderResponse order)
    {
        Response.Headers["Retry-After"] = "2";
        return AcceptedAtAction(nameof(Get), new { id = order.OrderId }, order);
    }
}
