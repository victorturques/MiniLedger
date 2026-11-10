namespace MiniLedger.Application.DTOs;

public record DepositResponse(string AccountNumber, string HolderName, decimal Amount, decimal NewBalance, Guid TransactionId, DateTime CreatedAt);
