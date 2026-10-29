namespace Order.Api.Entities;
public sealed record OrderItem(Guid OrderItemId, Guid ProductId, int Quantity);
public sealed record Order(Guid OrderId, Guid UserId, string OrderStatus, DateTime CreatedAt, IReadOnlyList<OrderItem> Items);
