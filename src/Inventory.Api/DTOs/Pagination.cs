using System.ComponentModel.DataAnnotations;
namespace Inventory.Api.DTOs;

public sealed class PageQuery
{
    [Range(1, int.MaxValue)] public int PageNumber { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
}
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int PageNumber, int PageSize, long TotalCount);
