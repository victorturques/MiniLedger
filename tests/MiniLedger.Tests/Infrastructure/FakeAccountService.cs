using MiniLedger.Application.DTOs;
using MiniLedger.Application.Interfaces;

namespace MiniLedger.Tests.Infrastructure;

/// <summary>
/// Fake baseado em delegates: cada teste configura apenas os métodos que o controller
/// realmente chama. Métodos não configurados lançam NotImplementedException, o que
/// evita que um teste passe por acidente chamando algo que não deveria.
/// </summary>
public sealed class FakeAccountService : IAccountService
{
    public Func<CreateAccountRequest, Task<AccountResponse>> CreateAccount { get; set; } =
        _ => throw new NotImplementedException();

    public Func<string, Task<AccountResponse?>> GetAccountByNumber { get; set; } =
        _ => throw new NotImplementedException();

    public Func<ClientAccessRequest, Task<AuthResponse?>> ValidateClientAccess { get; set; } =
        _ => throw new NotImplementedException();

    public Func<DepositRequest, Task<DepositResponse?>> Deposit { get; set; } =
        _ => throw new NotImplementedException();

    public Func<string, int, int, Task<PagedResult<TransactionResponse>>> GetTransactions { get; set; } =
        (_, _, _) => throw new NotImplementedException();

    public Task<AccountResponse> CreateAccountAsync(CreateAccountRequest request, CancellationToken cancellationToken = default)
        => CreateAccount(request);

    public Task<AccountResponse?> GetAccountByNumberAsync(string accountNumber, CancellationToken cancellationToken = default)
        => GetAccountByNumber(accountNumber);

    public Task<AuthResponse?> ValidateClientAccessAsync(ClientAccessRequest request, CancellationToken cancellationToken = default)
        => ValidateClientAccess(request);

    public Task<DepositResponse?> DepositAsync(DepositRequest request, CancellationToken cancellationToken = default)
        => Deposit(request);

    public Task<PagedResult<TransactionResponse>> GetTransactionsByAccountNumberAsync(
        string accountNumber,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
        => GetTransactions(accountNumber, page, pageSize);
}
