namespace Inventory.Api.Entities;
public sealed record Product(Guid ProductId, string Name, int StockQty, bool IsActive, DateTime CreatedAt, DateTime? UpdatedAt);
