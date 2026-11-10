using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MiniLedger.Domain.Entities;
using MiniLedger.Infrastructure.Data;

namespace MiniLedger.Tests.Infrastructure;

/// <summary>
/// Cria um banco SQLite em memória isolado por teste.
/// A conexão fica aberta durante toda a vida do objeto para que o banco não seja descartado.
/// As migrations reais são aplicadas (em vez de EnsureCreated) para que o schema testado
/// seja exatamente o mesmo usado em produção.
/// </summary>
public sealed class SqliteTestDb : IAsyncDisposable
{
    private static readonly string DefaultPasswordHash = BCrypt.Net.BCrypt.HashPassword("1234");

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public AppDbContext Context { get; }

    public SqliteTestDb()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        Context = new AppDbContext(_options);
        Context.Database.Migrate();
    }

    /// <summary>
    /// Cria um segundo contexto ligado à mesma conexão/banco.
    /// Útil para simular dois clientes concorrentes: cada contexto tem seu próprio
    /// ChangeTracker e enxerga uma cópia diferente do estado da conta.
    /// </summary>
    public AppDbContext CreateContext() => new(_options);

    public async Task<Account> SeedAccountAsync(
        string accountNumber,
        decimal balance = 0m,
        string password = "1234",
        string holderName = "Titular Teste")
    {
        var account = new Account
        {
            HolderName = holderName,
            AccountNumber = accountNumber,
            PasswordHash = password == "1234"
                ? DefaultPasswordHash
                : BCrypt.Net.BCrypt.HashPassword(password),
            Balance = balance,
            CreatedAt = DateTime.UtcNow
        };

        Context.Accounts.Add(account);
        await Context.SaveChangesAsync();

        return account;
    }

    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
