namespace MiniLedger.Application.DTOs;

public record ClientAccessResponse(Guid Id, string HolderName, string AccountNumber, decimal Balance, DateTime CreatedAt);
