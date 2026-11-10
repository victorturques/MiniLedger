namespace MiniLedger.Application.DTOs;

public record AuthResponse(Guid Id, string HolderName, string AccountNumber, decimal Balance, DateTime CreatedAt, string Token);
