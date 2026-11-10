using Microsoft.EntityFrameworkCore;
using MiniLedger.Application.DTOs;
using MiniLedger.Application.Interfaces;
using MiniLedger.Domain.Entities;
using MiniLedger.Domain.Enums;

namespace MiniLedger.Application.Services;

public class TransferService : ITransferService
{
    private readonly DbContext _context;
    private readonly DbSet<Account> _accounts;
    private readonly DbSet<Transaction> _transactions;

    public TransferService(DbContext context)
    {
        _context = context;
        _accounts = _context.Set<Account>();
        _transactions = _context.Set<Transaction>();
    }

    public async Task<TransferResponse> TransferAsync(TransferRequest request, CancellationToken cancellationToken = default)
    {
        await using var dbTransaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var fromAccount = await _accounts
                .FirstOrDefaultAsync(a => a.AccountNumber == request.FromAccountNumber, cancellationToken);

            var toAccount = await _accounts
                .FirstOrDefaultAsync(a => a.AccountNumber == request.ToAccountNumber, cancellationToken);

            if (fromAccount is null)
                throw new InvalidOperationException("Conta de origem não encontrada.");

            if (toAccount is null)
                throw new InvalidOperationException("Conta de destino não encontrada.");

            if (fromAccount.AccountNumber == toAccount.AccountNumber)
                throw new InvalidOperationException("Não é possível transferir para a mesma conta.");

            if (request.Amount <= 0)
                throw new InvalidOperationException("O valor da transferência deve ser maior que 0.");

            if (fromAccount.Balance < request.Amount)
                throw new InvalidOperationException("Saldo insuficiente.");

            var transferId = Guid.NewGuid();
            var now = DateTime.UtcNow;

            fromAccount.Balance -= request.Amount;
            toAccount.Balance += request.Amount;

            _transactions.Add(new Transaction
            {
                AccountId = fromAccount.Id,
                TransferId = transferId,
                Type = TransactionType.Debit,
                Amount = request.Amount,
                CounterpartyAccountNumber = toAccount.AccountNumber,
                CreatedAt = now
            });

            _transactions.Add(new Transaction
            {
                AccountId = toAccount.Id,
                TransferId = transferId,
                Type = TransactionType.Credit,
                Amount = request.Amount,
                CounterpartyAccountNumber = fromAccount.AccountNumber,
                CreatedAt = now
            });

            await _context.SaveChangesAsync(cancellationToken);
            await dbTransaction.CommitAsync(cancellationToken);

            return new TransferResponse(
                transferId,
                fromAccount.AccountNumber,
                toAccount.AccountNumber,
                request.Amount,
                "Success",
                now);
        }
        catch (DbUpdateConcurrencyException)
        {
            await dbTransaction.RollbackAsync(cancellationToken);
            throw new InvalidOperationException("Operação concorrente detectada. Tente novamente.");
        }
        catch
        {
            await dbTransaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
