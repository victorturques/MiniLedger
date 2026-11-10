namespace MiniLedger.Application.DTOs;

public record PagedResult<T>(
    IEnumerable<T> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
