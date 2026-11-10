namespace MiniLedger.Application.DTOs;

public record TransferResponse(
    Guid TransferId,
    string FromAccountNumber,
    string ToAccountNumber,
    decimal Amount,
    string Status,
    DateTime CreatedAt);
