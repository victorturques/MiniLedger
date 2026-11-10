using Microsoft.EntityFrameworkCore;
using MiniLedger.Application.DTOs;
using MiniLedger.Application.Services;
using MiniLedger.Domain.Enums;
using MiniLedger.Tests.Infrastructure;

namespace MiniLedger.Tests.Services;

public class TransferServiceTests
{
    [Fact]
    public async Task Transfer_Should_Move_Balance_And_Create_Debit_And_Credit_Transactions()
    {
        await using var db = new SqliteTestDb();
        var from = await db.SeedAccountAsync("00000001", 100m);
        var to = await db.SeedAccountAsync("00000002", 20m);
        var service = new TransferService(db.Context);

        var response = await service.TransferAsync(new TransferRequest("00000001", "00000002", 30m));

        Assert.Equal("Success", response.Status);
        Assert.Equal(30m, response.Amount);
        Assert.Equal("00000001", response.FromAccountNumber);
        Assert.Equal("00000002", response.ToAccountNumber);

        var balances = await db.Context.Accounts
            .AsNoTracking()
            .Where(a => a.Id == from.Id || a.Id == to.Id)
            .ToDictionaryAsync(a => a.AccountNumber, a => a.Balance);

        Assert.Equal(70m, balances["00000001"]);
        Assert.Equal(50m, balances["00000002"]);

        var transactions = await db.Context.Transactions.AsNoTracking().ToListAsync();
        Assert.Equal(2, transactions.Count);

        var debit = transactions.Single(t => t.Type == TransactionType.Debit);
        Assert.Equal(from.Id, debit.AccountId);
        Assert.Equal(30m, debit.Amount);
        Assert.Equal("00000002", debit.CounterpartyAccountNumber);

        var credit = transactions.Single(t => t.Type == TransactionType.Credit);
        Assert.Equal(to.Id, credit.AccountId);
        Assert.Equal(30m, credit.Amount);
        Assert.Equal("00000001", credit.CounterpartyAccountNumber);
    }

    [Fact]
    public async Task Transfer_Should_Use_Same_TransferId_For_Both_Transactions()
    {
        await using var db = new SqliteTestDb();
        await db.SeedAccountAsync("00000001", 100m);
        await db.SeedAccountAsync("00000002", 0m);
        var service = new TransferService(db.Context);

        var response = await service.TransferAsync(new TransferRequest("00000001", "00000002", 10m));

        var transactions = await db.Context.Transactions.AsNoTracking().ToListAsync();
        Assert.Equal(2, transactions.Count);
        Assert.All(transactions, t => Assert.Equal(response.TransferId, t.TransferId));
    }

    [Fact]
    public async Task Transfer_Should_Throw_When_Source_Account_Does_Not_Exist()
    {
        await using var db = new SqliteTestDb();
        await db.SeedAccountAsync("00000002", 0m);
        var service = new TransferService(db.Context);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.TransferAsync(new TransferRequest("00000099", "00000002", 10m)));

        Assert.Contains("origem", ex.Message);
    }

    [Fact]
    public async Task Transfer_Should_Throw_When_Target_Account_Does_Not_Exist()
    {
        await using var db = new SqliteTestDb();
        await db.SeedAccountAsync("00000001", 100m);
        var service = new TransferService(db.Context);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.TransferAsync(new TransferRequest("00000001", "00000099", 10m)));

        Assert.Contains("destino", ex.Message);
    }

    [Fact]
    public async Task Transfer_Should_Throw_When_Source_And_Target_Are_The_Same()
    {
        await using var db = new SqliteTestDb();
        await db.SeedAccountAsync("00000001", 100m);
        var service = new TransferService(db.Context);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.TransferAsync(new TransferRequest("00000001", "00000001", 10m)));

        Assert.Contains("mesma conta", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Transfer_Should_Throw_When_Amount_Is_Not_Positive(decimal amount)
    {
        await using var db = new SqliteTestDb();
        await db.SeedAccountAsync("00000001", 100m);
        await db.SeedAccountAsync("00000002", 0m);
        var service = new TransferService(db.Context);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.TransferAsync(new TransferRequest("00000001", "00000002", amount)));

        Assert.Contains("maior que 0", ex.Message);
    }

    [Fact]
    public async Task Transfer_Should_Throw_When_Balance_Is_Insufficient()
    {
        await using var db = new SqliteTestDb();
        await db.SeedAccountAsync("00000001", 10m);
        await db.SeedAccountAsync("00000002", 0m);
        var service = new TransferService(db.Context);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.TransferAsync(new TransferRequest("00000001", "00000002", 50m)));

        Assert.Contains("Saldo insuficiente", ex.Message);
    }

    [Fact]
    public async Task Transfer_Should_Not_Change_Balances_When_It_Fails()
    {
        await using var db = new SqliteTestDb();
        var from = await db.SeedAccountAsync("00000001", 10m);
        var to = await db.SeedAccountAsync("00000002", 0m);
        var service = new TransferService(db.Context);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.TransferAsync(new TransferRequest("00000001", "00000002", 50m)));

        var balances = await db.Context.Accounts
            .AsNoTracking()
            .Where(a => a.Id == from.Id || a.Id == to.Id)
            .ToDictionaryAsync(a => a.AccountNumber, a => a.Balance);

        Assert.Equal(10m, balances["00000001"]);
        Assert.Equal(0m, balances["00000002"]);
        Assert.Equal(0, await db.Context.Transactions.CountAsync());
    }

    [Fact]
    public async Task Transfer_Should_Throw_When_Accounts_Were_Updated_By_Another_Context()
    {
        await using var db = new SqliteTestDb();
        await db.SeedAccountAsync("00000001", 100m);
        await db.SeedAccountAsync("00000002", 0m);

        // Segundo cliente credita na conta de destino e incrementa a Version.
        await using (var other = db.CreateContext())
        {
            var otherAccount = await other.Accounts.SingleAsync(a => a.AccountNumber == "00000002");
            otherAccount.Balance += 5m;
            await other.SaveChangesAsync();
        }

        // O contexto original ainda tem a Version antiga da conta de destino (0).
        var service = new TransferService(db.Context);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.TransferAsync(new TransferRequest("00000001", "00000002", 30m)));

        Assert.Contains("concorrente", ex.Message);

        var balances = await db.Context.Accounts
            .AsNoTracking()
            .Where(a => a.AccountNumber == "00000001" || a.AccountNumber == "00000002")
            .ToDictionaryAsync(a => a.AccountNumber, a => a.Balance);

        Assert.Equal(100m, balances["00000001"]);
        Assert.Equal(5m, balances["00000002"]);
        Assert.Equal(0, await db.Context.Transactions.CountAsync());
    }

    [Fact]
    public async Task Transfer_Should_Accumulate_Multiple_Transfers()
    {
        await using var db = new SqliteTestDb();
        var from = await db.SeedAccountAsync("00000001", 100m);
        var to = await db.SeedAccountAsync("00000002", 0m);
        var service = new TransferService(db.Context);

        await service.TransferAsync(new TransferRequest("00000001", "00000002", 30m));
        await service.TransferAsync(new TransferRequest("00000001", "00000002", 20m));

        var balances = await db.Context.Accounts
            .AsNoTracking()
            .Where(a => a.Id == from.Id || a.Id == to.Id)
            .ToDictionaryAsync(a => a.AccountNumber, a => a.Balance);

        Assert.Equal(50m, balances["00000001"]);
        Assert.Equal(50m, balances["00000002"]);
        Assert.Equal(4, await db.Context.Transactions.CountAsync());
    }
}
