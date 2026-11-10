using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using MiniLedger.Domain.Entities;

namespace MiniLedger.Application.Interfaces;

public interface IAppDbContext
{
    DbSet<Account> Accounts { get; }
    DbSet<Transaction> Transactions { get; }
    DatabaseFacade Database { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    DbSet<T> Set<T>() where T : class;
}
