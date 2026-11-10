namespace MiniLedger.Application.DTOs;

public record AccountResponse(Guid Id, string HolderName, string AccountNumber, decimal Balance, DateTime CreatedAt);
