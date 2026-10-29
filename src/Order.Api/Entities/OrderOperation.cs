using Order.Api.DTOs;
namespace Order.Api.Entities;

public sealed record OrderOperation(Guid OperationId, Guid OrderId, Guid UserId, string OperationType,
    IReadOnlyList<StockItem> Items, string CorrelationId, string State, int? ErrorStatus, string? ErrorDetail);
