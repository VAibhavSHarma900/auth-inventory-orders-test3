using System.ComponentModel.DataAnnotations;
using Order.Api.Entities;
namespace Order.Api.DTOs;

public sealed class OrderItemRequest : IValidatableObject
{
    [Required] public Guid? ProductId { get; init; }
    [Required, Range(1, int.MaxValue)] public int? Quantity { get; init; }
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (ProductId == Guid.Empty) yield return new("ProductId cannot be empty.", [nameof(ProductId)]);
    }
}
public sealed class OrderRequest
{
    [Required, MinLength(1)] public List<OrderItemRequest> Items { get; init; } = [];
}
public sealed record OrderItemResponse(Guid OrderItemId, Guid ProductId, int Quantity);
public sealed record OrderResponse(Guid OrderId, Guid UserId, string OrderStatus, DateTime CreatedAt, IReadOnlyList<OrderItemResponse> Items)
{
    public static OrderResponse From(Entities.Order order) => new(order.OrderId, order.UserId, order.OrderStatus, order.CreatedAt,
        order.Items.Select(i => new OrderItemResponse(i.OrderItemId, i.ProductId, i.Quantity)).ToArray());
}
public sealed record StockItem(Guid ProductId, int Quantity);
public sealed record ProductSnapshot(Guid ProductId, int StockQty, bool IsActive);
