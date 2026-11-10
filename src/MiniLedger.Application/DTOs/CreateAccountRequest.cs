namespace MiniLedger.Application.DTOs;

public record CreateAccountRequest(string HolderName, string Password, string ConfirmPassword, decimal InitialBalance = 0);
