using Microsoft.EntityFrameworkCore;
using MiniLedger.Application.DTOs;
using MiniLedger.Application.Services;
using MiniLedger.Domain.Entities;
using MiniLedger.Domain.Enums;
using MiniLedger.Tests.Infrastructure;

namespace MiniLedger.Tests.Services;

public class AccountServiceTests
{
    private static AccountService CreateService(SqliteTestDb db, FakeTokenService? tokenService = null)
        => new(db.Context, tokenService ?? new FakeTokenService());

    // ---------------------------------------------------------------------
    // CreateAccountAsync
    // ---------------------------------------------------------------------

    [Fact]
    public async Task CreateAccount_Should_Generate_Sequential_AccountNumbers()
    {
        await using var db = new SqliteTestDb();
        var service = CreateService(db);

        var first = await service.CreateAccountAsync(new CreateAccountRequest("Ana", "1234", "1234"));
        var second = await service.CreateAccountAsync(new CreateAccountRequest("Bia", "1234", "1234"));

        Assert.Equal("00000001", first.AccountNumber);
        Assert.Equal("00000002", second.AccountNumber);
    }

    [Fact]
    public async Task CreateAccount_Should_Hash_Password_And_Trim_HolderName()
    {
        await using var db = new SqliteTestDb();
        var service = CreateService(db);

        var response = await service.CreateAccountAsync(new CreateAccountRequest("  Ana Silva  ", "1234", "1234"));

        Assert.Equal("Ana Silva", response.HolderName);

        var stored = await db.Context.Accounts.AsNoTracking().SingleAsync();
        Assert.NotEqual("1234", stored.PasswordHash);
        Assert.True(BCrypt.Net.BCrypt.Verify("1234", stored.PasswordHash));
        Assert.Equal("00000001", stored.AccountNumber);
    }

    [Fact]
    public async Task CreateAccount_Should_Create_Initial_Credit_Transaction_When_Balance_Is_Positive()
    {
        await using var db = new SqliteTestDb();
        var service = CreateService(db);

        var response = await service.CreateAccountAsync(new CreateAccountRequest("Ana", "1234", "1234", 150.75m));

        Assert.Equal(150.75m, response.Balance);

        var transaction = await db.Context.Transactions.AsNoTracking().SingleAsync();
        Assert.Equal(TransactionType.Credit, transaction.Type);
        Assert.Equal(150.75m, transaction.Amount);
        Assert.Equal("SALDO INICIAL", transaction.CounterpartyAccountNumber);
        Assert.Equal(response.Id, transaction.AccountId);
    }

    [Fact]
    public async Task CreateAccount_Should_Not_Create_Transaction_When_Balance_Is_Zero()
    {
        await using var db = new SqliteTestDb();
        var service = CreateService(db);

        await service.CreateAccountAsync(new CreateAccountRequest("Ana", "1234", "1234", 0m));

        Assert.Equal(0, await db.Context.Transactions.CountAsync());
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("1234", "")]
    [InlineData("123", "123")]
    [InlineData("1234", "4321")]
    public async Task CreateAccount_Should_Throw_When_Password_Is_Invalid(string password, string confirmPassword)
    {
        await using var db = new SqliteTestDb();
        var service = CreateService(db);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.CreateAccountAsync(new CreateAccountRequest("Ana", password, confirmPassword)));
    }

    [Fact]
    public async Task CreateAccount_Should_Not_Persist_Anything_When_Validation_Fails()
    {
        await using var db = new SqliteTestDb();
        var service = CreateService(db);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.CreateAccountAsync(new CreateAccountRequest("Ana", "1234", "9999")));

        Assert.Equal(0, await db.Context.Accounts.CountAsync());
        Assert.Equal(0, await db.Context.Transactions.CountAsync());
    }

    [Fact]
    public async Task CreateAccount_Should_Reject_Negative_Initial_Balance()
    {
        await using var db = new SqliteTestDb();
        var service = CreateService(db);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.CreateAccountAsync(new CreateAccountRequest("Ana", "1234", "1234", -10m)));

        Assert.Equal(0, await db.Context.Accounts.CountAsync());
        Assert.Equal(0, await db.Context.Transactions.CountAsync());
    }

    [Fact]
    public async Task CreateAccount_Should_Continue_After_Highest_8_Digit_Number()
    {
        await using var db = new SqliteTestDb();
        await db.SeedAccountAsync("00000009");
        await db.SeedAccountAsync("00000010");
        var service = CreateService(db);

        var response = await service.CreateAccountAsync(new CreateAccountRequest("Ana", "1234", "1234"));

        Assert.Equal("00000011", response.AccountNumber);
    }

    [Fact]
    public async Task CreateAccount_Should_Ignore_Non_Numeric_Numbers_When_Generating()
    {
        await using var db = new SqliteTestDb();
        await db.SeedAccountAsync("ABCDEFGH");
        await db.SeedAccountAsync("00000001");
        var service = CreateService(db);

        var response = await service.CreateAccountAsync(new CreateAccountRequest("Ana", "1234", "1234"));

        Assert.Equal("00000002", response.AccountNumber);
    }

    [Fact]
    public async Task CreateAccount_Should_Start_At_One_When_Only_Non_Numeric_Numbers_Exist()
    {
        await using var db = new SqliteTestDb();
        await db.SeedAccountAsync("ABCDEFGH");
        var service = CreateService(db);

        var response = await service.CreateAccountAsync(new CreateAccountRequest("Ana", "1234", "1234"));

        Assert.Equal("00000001", response.AccountNumber);
    }

    [Fact]
    public async Task CreateAccount_Should_Throw_When_Account_Number_Limit_Is_Reached()
    {
        await using var db = new SqliteTestDb();
        await db.SeedAccountAsync("99999999");
        var service = CreateService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateAccountAsync(new CreateAccountRequest("Ana", "1234", "1234")));

        Assert.Contains("limite", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await db.Context.Accounts.CountAsync());
    }

    // ---------------------------------------------------------------------
    // GetAccountByNumberAsync
    // ---------------------------------------------------------------------

    [Fact]
    public async Task GetAccountByNumber_Should_Return_Account_When_It_Exists()
    {
        await using var db = new SqliteTestDb();
        await db.SeedAccountAsync("00000001", 42m, holderName: "Ana");
        var service = CreateService(db);

        var response = await service.GetAccountByNumberAsync("00000001");

        Assert.NotNull(response);
        Assert.Equal("Ana", response!.HolderName);
        Assert.Equal(42m, response.Balance);
    }

    [Fact]
    public async Task GetAccountByNumber_Should_Return_Null_When_It_Does_Not_Exist()
    {
        await using var db = new SqliteTestDb();
        var service = CreateService(db);

        Assert.Null(await service.GetAccountByNumberAsync("00000099"));
    }

    // ---------------------------------------------------------------------
    // ValidateClientAccessAsync
    // ---------------------------------------------------------------------

    [Fact]
    public async Task ValidateClientAccess_Should_Return_AuthResponse_And_Token_When_Credentials_Are_Valid()
    {
        await using var db = new SqliteTestDb();
        var tokenService = new FakeTokenService();
        var service = CreateService(db, tokenService);
        var account = await db.SeedAccountAsync("00000001", 30m, password: "senha123");

        var response = await service.ValidateClientAccessAsync(new ClientAccessRequest("00000001", "senha123"));

        Assert.NotNull(response);
        Assert.Equal(account.Id, response!.Id);
        Assert.Equal(FakeTokenService.Token, response.Token);
        Assert.Equal(1, tokenService.Calls);
        Assert.Equal("00000001", tokenService.LastAccount!.AccountNumber);
    }

    [Fact]
    public async Task ValidateClientAccess_Should_Return_Null_When_Password_Is_Wrong()
    {
        await using var db = new SqliteTestDb();
        var tokenService = new FakeTokenService();
        var service = CreateService(db, tokenService);
        await db.SeedAccountAsync("00000001", 30m, password: "senha123");

        var response = await service.ValidateClientAccessAsync(new ClientAccessRequest("00000001", "errada"));

        Assert.Null(response);
        Assert.Equal(0, tokenService.Calls);
    }

    [Fact]
    public async Task ValidateClientAccess_Should_Return_Null_When_Account_Does_Not_Exist()
    {
        await using var db = new SqliteTestDb();
        var service = CreateService(db);

        Assert.Null(await service.ValidateClientAccessAsync(new ClientAccessRequest("00000099", "1234")));
    }

    [Theory]
    [InlineData("", "1234")]
    [InlineData("   ", "1234")]
    [InlineData("00000001", "")]
    [InlineData("00000001", "   ")]
    public async Task ValidateClientAccess_Should_Return_Null_When_Input_Is_Empty(string accountNumber, string password)
    {
        await using var db = new SqliteTestDb();
        await db.SeedAccountAsync("00000001");
        var service = CreateService(db);

        Assert.Null(await service.ValidateClientAccessAsync(new ClientAccessRequest(accountNumber, password)));
    }

    // ---------------------------------------------------------------------
    // DepositAsync
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Deposit_Should_Increment_Balance_And_Create_Credit_Transaction()
    {
        await using var db = new SqliteTestDb();
        var account = await db.SeedAccountAsync("00000001", 100m);
        var service = CreateService(db);

        var response = await service.DepositAsync(new DepositRequest("00000001", 25.50m));

        Assert.NotNull(response);
        Assert.Equal(125.50m, response!.NewBalance);
        Assert.Equal(25.50m, response.Amount);
        Assert.Equal(account.HolderName, response.HolderName);

        var stored = await db.Context.Accounts.AsNoTracking().SingleAsync(a => a.Id == account.Id);
        Assert.Equal(125.50m, stored.Balance);

        var transaction = await db.Context.Transactions.AsNoTracking().SingleAsync();
        Assert.Equal(TransactionType.Credit, transaction.Type);
        Assert.Equal(25.50m, transaction.Amount);
        Assert.Equal("DEPÓSITO", transaction.CounterpartyAccountNumber);
        Assert.Equal(response.TransactionId, transaction.Id);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public async Task Deposit_Should_Throw_When_Amount_Is_Not_Positive(decimal amount)
    {
        await using var db = new SqliteTestDb();
        await db.SeedAccountAsync("00000001", 100m);
        var service = CreateService(db);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.DepositAsync(new DepositRequest("00000001", amount)));
    }

    [Fact]
    public async Task Deposit_Should_Return_Null_When_Account_Does_Not_Exist()
    {
        await using var db = new SqliteTestDb();
        var service = CreateService(db);

        var response = await service.DepositAsync(new DepositRequest("00000099", 10m));

        Assert.Null(response);
        Assert.Equal(0, await db.Context.Transactions.CountAsync());
    }

    [Fact]
    public async Task Deposit_Should_Throw_When_Account_Was_Updated_By_Another_Context()
    {
        await using var db = new SqliteTestDb();
        await db.SeedAccountAsync("00000001", 100m);

        // Segundo cliente altera a conta e incrementa a Version no banco.
        await using (var other = db.CreateContext())
        {
            var otherAccount = await other.Accounts.SingleAsync(a => a.AccountNumber == "00000001");
            otherAccount.Balance += 50m;
            await other.SaveChangesAsync();
        }

        // O contexto original continua com a Version antiga em cache (0), então o
        // UPDATE ... WHERE Version = 0 não afeta nenhuma linha.
        var service = CreateService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DepositAsync(new DepositRequest("00000001", 10m)));

        Assert.Contains("concorrente", ex.Message);

        var stored = await db.Context.Accounts.AsNoTracking().SingleAsync(a => a.AccountNumber == "00000001");
        Assert.Equal(150m, stored.Balance);
        Assert.Equal(1, stored.Version);
        Assert.Equal(0, await db.Context.Transactions.CountAsync());
    }

    // ---------------------------------------------------------------------
    // GetTransactionsByAccountNumberAsync
    // ---------------------------------------------------------------------

    [Fact]
    public async Task GetTransactions_Should_Return_Paged_Transactions_Ordered_By_Date_Desc()
    {
        await using var db = new SqliteTestDb();
        var account = await db.SeedAccountAsync("00000001", 0m);
        var now = DateTime.UtcNow;

        db.Context.Transactions.AddRange(
            NewTransaction(account.Id, 10m, now.AddMinutes(-3)),
            NewTransaction(account.Id, 20m, now.AddMinutes(-2)),
            NewTransaction(account.Id, 30m, now.AddMinutes(-1)));
        await db.Context.SaveChangesAsync();

        var service = CreateService(db);

        var firstPage = await service.GetTransactionsByAccountNumberAsync("00000001", page: 1, pageSize: 2);

        Assert.Equal(3, firstPage.TotalCount);
        Assert.Equal(2, firstPage.TotalPages);
        Assert.Equal(1, firstPage.Page);
        Assert.Equal(2, firstPage.PageSize);
        var firstPageAmounts = firstPage.Items.Select(t => t.Amount).ToList();
        Assert.Equal(new[] { 30m, 20m }, firstPageAmounts);

        var secondPage = await service.GetTransactionsByAccountNumberAsync("00000001", page: 2, pageSize: 2);

        var secondPageAmounts = secondPage.Items.Select(t => t.Amount).ToList();
        Assert.Equal(new[] { 10m }, secondPageAmounts);
    }

    [Fact]
    public async Task GetTransactions_Should_Not_Return_Transactions_From_Other_Accounts()
    {
        await using var db = new SqliteTestDb();
        var account = await db.SeedAccountAsync("00000001", 0m);
        var other = await db.SeedAccountAsync("00000002", 0m);
        var now = DateTime.UtcNow;

        db.Context.Transactions.AddRange(
            NewTransaction(account.Id, 10m, now),
            NewTransaction(other.Id, 99m, now));
        await db.Context.SaveChangesAsync();

        var service = CreateService(db);

        var page = await service.GetTransactionsByAccountNumberAsync("00000001");

        Assert.Equal(1, page.TotalCount);
        Assert.Equal(10m, page.Items.Single().Amount);
    }

    [Fact]
    public async Task GetTransactions_Should_Return_Empty_Page_When_Account_Does_Not_Exist()
    {
        await using var db = new SqliteTestDb();
        var service = CreateService(db);

        var page = await service.GetTransactionsByAccountNumberAsync("00000099");

        Assert.Equal(0, page.TotalCount);
        Assert.Equal(0, page.TotalPages);
        Assert.Empty(page.Items);
    }

    [Theory]
    [InlineData(0, 10, 1, 10)]
    [InlineData(-3, 10, 1, 10)]
    [InlineData(1, 0, 1, 20)]
    [InlineData(1, -5, 1, 20)]
    [InlineData(1, 500, 1, 20)]
    [InlineData(0, 0, 1, 20)]
    public async Task GetTransactions_Should_Normalize_Invalid_Pagination(
        int page,
        int pageSize,
        int expectedPage,
        int expectedPageSize)
    {
        await using var db = new SqliteTestDb();
        var account = await db.SeedAccountAsync("00000001");
        var now = DateTime.UtcNow;

        db.Context.Transactions.AddRange(
            NewTransaction(account.Id, 10m, now.AddMinutes(-1)),
            NewTransaction(account.Id, 20m, now));
        await db.Context.SaveChangesAsync();

        var service = CreateService(db);

        var result = await service.GetTransactionsByAccountNumberAsync("00000001", page, pageSize);

        Assert.Equal(expectedPage, result.Page);
        Assert.Equal(expectedPageSize, result.PageSize);
        Assert.Equal(2, result.TotalCount);
        Assert.Equal(1, result.TotalPages);
        Assert.Equal(2, result.Items.Count());
    }

    private static Transaction NewTransaction(Guid accountId, decimal amount, DateTime createdAt) => new()
    {
        AccountId = accountId,
        Type = TransactionType.Credit,
        Amount = amount,
        TransferId = Guid.NewGuid(),
        CounterpartyAccountNumber = "00000002",
        CreatedAt = createdAt
    };
}
