namespace MiniLedger.Application.DTOs;

public record DepositRequest(string AccountNumber, decimal Amount);
