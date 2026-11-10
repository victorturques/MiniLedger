using Microsoft.EntityFrameworkCore;
using MiniLedger.Application.DTOs;
using MiniLedger.Application.Interfaces;
using MiniLedger.Domain.Entities;
using MiniLedger.Domain.Enums;

namespace MiniLedger.Application.Services;

public class AccountService : IAccountService
{
    private readonly IAppDbContext _context;
    private readonly DbSet<Account> _accounts;
    private readonly ITokenService _tokenService;

    public AccountService(IAppDbContext context, ITokenService tokenService)
    {
        _context = context;
        _accounts = _context.Accounts;
        _tokenService = tokenService;
    }

    public async Task<AccountResponse> CreateAccountAsync(CreateAccountRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Password))
            throw new ArgumentException("A senha é obrigatória.");

        if (request.Password != request.ConfirmPassword)
            throw new ArgumentException("As senhas não coincidem.");

        if (request.Password.Length < 4)
            throw new ArgumentException("A senha deve ter pelo menos 4 caracteres.");

        if (request.InitialBalance < 0)
            throw new ArgumentException("O saldo inicial não pode ser negativo.");

        const int maxAttempts = 3;

        for (var attempt = 1; ; attempt++)
        {
            var accountNumber = await GenerateAccountNumberAsync(cancellationToken);

            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

            Account? account = null;

            try
            {
                account = new Account
                {
                    HolderName = request.HolderName?.Trim() ?? string.Empty,
                    AccountNumber = accountNumber,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
                    Balance = request.InitialBalance,
                    CreatedAt = DateTime.UtcNow
                };

                _accounts.Add(account);
                await _context.SaveChangesAsync(cancellationToken);

                if (request.InitialBalance > 0)
                {
                    var initialTransaction = new Transaction
                    {
                        Id = Guid.NewGuid(),
                        AccountId = account.Id,
                        Account = account,
                        Type = TransactionType.Credit,
                        Amount = request.InitialBalance,
                        TransferId = Guid.NewGuid(),
                        CounterpartyAccountNumber = "SALDO INICIAL",
                        CreatedAt = DateTime.UtcNow
                    };

                    _context.Set<Transaction>().Add(initialTransaction);
                    await _context.SaveChangesAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);

                return new AccountResponse(account.Id, account.HolderName, account.AccountNumber, account.Balance, account.CreatedAt);
            }
            catch (DbUpdateException ex) when (attempt < maxAttempts && IsDuplicateAccountNumber(ex))
            {
                // Outro cadastro simultâneo usou o mesmo número: descarta a entidade
                // e tenta de novo com o próximo número livre.
                await transaction.RollbackAsync(cancellationToken);

                if (account is not null)
                    _accounts.Entry(account).State = EntityState.Detached;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
    }

    private static bool IsDuplicateAccountNumber(DbUpdateException ex)
        => ex.InnerException?.Message.Contains(
            "UNIQUE constraint failed: Accounts.AccountNumber",
            StringComparison.OrdinalIgnoreCase) == true;

    public async Task<AccountResponse?> GetAccountByNumberAsync(string accountNumber, CancellationToken cancellationToken = default)
    {
        var account = await _accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.AccountNumber == accountNumber, cancellationToken);

        if (account is null)
            return null;

        return new AccountResponse(account.Id, account.HolderName, account.AccountNumber, account.Balance, account.CreatedAt);
    }

    public async Task<AuthResponse?> ValidateClientAccessAsync(ClientAccessRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.AccountNumber) || string.IsNullOrWhiteSpace(request.Password))
            return null;

        var account = await _accounts
            .FirstOrDefaultAsync(a => a.AccountNumber == request.AccountNumber, cancellationToken);

        if (account is null)
            return null;

        var isValid = BCrypt.Net.BCrypt.Verify(request.Password, account.PasswordHash);
        if (!isValid)
            return null;

        var accountResponse = new AccountResponse(account.Id, account.HolderName, account.AccountNumber, account.Balance, account.CreatedAt);
        var token = _tokenService.GenerateToken(accountResponse);

        return new AuthResponse(account.Id, account.HolderName, account.AccountNumber, account.Balance, account.CreatedAt, token);
    }

    public async Task<DepositResponse?> DepositAsync(DepositRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Amount <= 0)
            throw new ArgumentException("O valor do depósito deve ser maior que zero.");

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var account = await _accounts
                .FirstOrDefaultAsync(a => a.AccountNumber == request.AccountNumber, cancellationToken);

            if (account is null)
                return null;

            account.Balance += request.Amount;

            var depositTransaction = new Transaction
            {
                Id = Guid.NewGuid(),
                AccountId = account.Id,
                Account = account,
                Type = TransactionType.Credit,
                Amount = request.Amount,
                TransferId = Guid.NewGuid(),
                CounterpartyAccountNumber = "DEPÓSITO",
                CreatedAt = DateTime.UtcNow
            };

            _context.Set<Transaction>().Add(depositTransaction);

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new DepositResponse(
                account.AccountNumber,
                account.HolderName,
                request.Amount,
                account.Balance,
                depositTransaction.Id,
                depositTransaction.CreatedAt
            );
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new InvalidOperationException("Operação concorrente detectada. Tente novamente.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<PagedResult<TransactionResponse>> GetTransactionsByAccountNumberAsync(string accountNumber, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        var account = await _accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.AccountNumber == accountNumber, cancellationToken);

        if (account is null)
            return new PagedResult<TransactionResponse>(Enumerable.Empty<TransactionResponse>(), page, pageSize, 0, 0);

        var query = _context.Transactions
            .AsNoTracking()
            .Where(t => t.AccountId == account.Id)
            .OrderByDescending(t => t.CreatedAt);

        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        var transactions = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = transactions.Select(t => new TransactionResponse(
            t.Id,
            t.Type,
            t.Amount,
            t.CounterpartyAccountNumber,
            t.TransferId,
            t.CreatedAt
        ));

        return new PagedResult<TransactionResponse>(items, page, pageSize, totalCount, totalPages);
    }

    private async Task<string> GenerateAccountNumberAsync(CancellationToken cancellationToken)
    {
        const long maxAccountNumber = 99_999_999;

        var numbers = await _accounts
            .AsNoTracking()
            .Select(a => a.AccountNumber)
            .ToListAsync(cancellationToken);

        var highest = numbers
            .Select(n => long.TryParse(n, out var value) && value is > 0 and <= maxAccountNumber ? value : 0)
            .DefaultIfEmpty(0)
            .Max();

        if (highest == maxAccountNumber)
            throw new InvalidOperationException("Limite de números de conta atingido.");

        return (highest + 1).ToString("D8");
    }
}
