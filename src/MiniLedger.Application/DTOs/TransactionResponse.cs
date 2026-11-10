using MiniLedger.Domain.Enums;

namespace MiniLedger.Application.DTOs;

public record TransactionResponse(
    Guid Id,
    TransactionType Type,
    decimal Amount,
    string? CounterpartyAccountNumber,
    Guid TransferId,
    DateTime CreatedAt);
