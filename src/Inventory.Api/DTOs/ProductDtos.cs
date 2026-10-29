using System.ComponentModel.DataAnnotations;
using Inventory.Api.Entities;
namespace Inventory.Api.DTOs;

public sealed class ProductRequest
{
    [Required, StringLength(150)]
    [RegularExpression(@"[^\u0000]*", ErrorMessage = "Name cannot contain null characters.")]
    public string Name { get; init; } = "";
    [Required, Range(0, int.MaxValue)] public int? StockQty { get; init; }
}
public sealed class QuantityRequest
{
    [Required, Range(1, int.MaxValue)] public int? Quantity { get; init; }
}
public sealed class StockItem : IValidatableObject
{
    [Required] public Guid? ProductId { get; init; }
    [Required, Range(1, int.MaxValue)] public int? Quantity { get; init; }
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (ProductId == Guid.Empty) yield return new("ProductId cannot be empty.", [nameof(ProductId)]);
    }
}
public class StockBatchRequest
{
    [Required, MinLength(1)] public List<StockItem> Items { get; init; } = [];
}
public sealed record StockResponse(Guid ProductId, int StockQty);
public sealed record ProductResponse(Guid ProductId, string Name, int StockQty, bool IsActive, DateTime CreatedAt, DateTime? UpdatedAt)
{
    public static ProductResponse From(Product p) => new(p.ProductId, p.Name, p.StockQty, p.IsActive, p.CreatedAt, p.UpdatedAt);
}

public sealed class StockOperationRequest : StockBatchRequest, IValidatableObject
{
    [Required] public Guid? OperationId { get; init; }
    [Required] public Guid? OrderId { get; init; }
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (OperationId == Guid.Empty) yield return new("OperationId cannot be empty.", [nameof(OperationId)]);
        if (OrderId == Guid.Empty) yield return new("OrderId cannot be empty.", [nameof(OrderId)]);
    }
}
public sealed record StockOperationResult(Guid OperationId, string Outcome, int? ErrorStatus, string? ErrorDetail);
