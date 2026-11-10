namespace MiniLedger.Application.DTOs;

public record TransferRequest(string FromAccountNumber, string ToAccountNumber, decimal Amount);
