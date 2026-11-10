using MiniLedger.Application.DTOs;

namespace MiniLedger.Application.Interfaces;

public interface IAccountService
{
    Task<AccountResponse> CreateAccountAsync(CreateAccountRequest request, CancellationToken cancellationToken = default);
    Task<AccountResponse?> GetAccountByNumberAsync(string accountNumber, CancellationToken cancellationToken = default);
    Task<AuthResponse?> ValidateClientAccessAsync(ClientAccessRequest request, CancellationToken cancellationToken = default);
    Task<DepositResponse?> DepositAsync(DepositRequest request, CancellationToken cancellationToken = default);
    Task<PagedResult<TransactionResponse>> GetTransactionsByAccountNumberAsync(string accountNumber, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default);
}