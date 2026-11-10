using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MiniLedger.Application.DTOs;
using MiniLedger.Domain.Enums;
using MiniLedger.Tests.Infrastructure;

namespace MiniLedger.Tests.Integration;

/// <summary>
/// Testes ponta a ponta: exercitam o pipeline HTTP real (autenticação, validação,
/// controllers, services, EF Core/SQLite e idempotência) via WebApplicationFactory.
/// </summary>
public class ApiEndToEndTests : IClassFixture<MiniLedgerWebAppFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly MiniLedgerWebAppFactory _factory;

    public ApiEndToEndTests(MiniLedgerWebAppFactory factory) => _factory = factory;

    private HttpClient CreateClient()
    {
        return _factory.CreateClient();
    }

    private static async Task<AccountResponse> CreateAccountAsync(
        HttpClient client,
        string holderName,
        decimal initialBalance = 0m)
    {
        var response = await client.PostAsJsonAsync(
            "/api/accounts",
            new CreateAccountRequest(holderName, "1234", "1234", initialBalance));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AccountResponse>(Json))!;
    }

    private static async Task<AuthResponse> LoginAsync(
        HttpClient client,
        string accountNumber,
        string password = "1234")
    {
        var response = await client.PostAsJsonAsync(
            "/api/accounts/access",
            new ClientAccessRequest(accountNumber, password));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
    }

    private static void Authorize(HttpClient client, string token)
        => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static HttpRequestMessage DepositRequest(string accountNumber, decimal amount, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/accounts/deposit")
        {
            Content = JsonContent.Create(new DepositRequest(accountNumber, amount))
        };

        if (idempotencyKey is not null)
            request.Headers.Add("Idempotency-Key", idempotencyKey);

        return request;
    }

    private static HttpRequestMessage TransferRequest(
        string fromAccountNumber,
        string toAccountNumber,
        decimal amount,
        string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/transfers")
        {
            Content = JsonContent.Create(new TransferRequest(fromAccountNumber, toAccountNumber, amount))
        };

        if (idempotencyKey is not null)
            request.Headers.Add("Idempotency-Key", idempotencyKey);

        return request;
    }

    private static async Task<AccountResponse> GetAccountAsync(HttpClient client, string accountNumber)
    {
        var response = await client.GetAsync($"/api/accounts/by-number/{accountNumber}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AccountResponse>(Json))!;
    }

    private async Task<AccountResponse> GetAccountAsAsync(string accountNumber)
    {
        using var client = CreateClient();
        var auth = await LoginAsync(client, accountNumber);
        Authorize(client, auth.Token);
        return await GetAccountAsync(client, accountNumber);
    }

    private static async Task<PagedResult<TransactionResponse>> GetStatementAsync(
        HttpClient client,
        string accountNumber)
    {
        var response = await client.GetAsync($"/api/accounts/by-number/{accountNumber}/transactions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PagedResult<TransactionResponse>>(Json))!;
    }

    [Fact]
    public async Task Full_Flow_Should_Create_Login_Deposit_Transfer_And_List_Statement()
    {
        using var client = CreateClient();

        var from = await CreateAccountAsync(client, "Ana", initialBalance: 100m);
        var to = await CreateAccountAsync(client, "Bia");

        var auth = await LoginAsync(client, from.AccountNumber);
        Assert.Equal(from.AccountNumber, auth.AccountNumber);
        Assert.False(string.IsNullOrWhiteSpace(auth.Token));

        Authorize(client, auth.Token);

        var depositResponse = await client.SendAsync(DepositRequest(from.AccountNumber, 50m));
        Assert.Equal(HttpStatusCode.OK, depositResponse.StatusCode);
        var deposit = (await depositResponse.Content.ReadFromJsonAsync<DepositResponse>(Json))!;
        Assert.Equal(150m, deposit.NewBalance);

        var transferResponse = await client.SendAsync(
            TransferRequest(from.AccountNumber, to.AccountNumber, 40m));
        Assert.Equal(HttpStatusCode.OK, transferResponse.StatusCode);
        var transfer = (await transferResponse.Content.ReadFromJsonAsync<TransferResponse>(Json))!;
        Assert.Equal("Success", transfer.Status);

        var fromAfter = await GetAccountAsync(client, from.AccountNumber);
        var toAfter = await GetAccountAsAsync(to.AccountNumber);
        Assert.Equal(110m, fromAfter.Balance);
        Assert.Equal(40m, toAfter.Balance);

        var statement = await GetStatementAsync(client, from.AccountNumber);
        Assert.Equal(3, statement.TotalCount);

        // Ordenado por data decrescente: débito 40, depósito 50 e saldo inicial 100.
        Assert.Equal(new[] { 40m, 50m, 100m }, statement.Items.Select(t => t.Amount));
        Assert.Equal(
            new[] { TransactionType.Debit, TransactionType.Credit, TransactionType.Credit },
            statement.Items.Select(t => t.Type));
    }

    [Fact]
    public async Task Protected_Endpoints_Should_Return_401_Without_Token()
    {
        using var client = CreateClient();
        var account = await CreateAccountAsync(client, "Ana", initialBalance: 50m);

        var deposit = await client.SendAsync(DepositRequest(account.AccountNumber, 10m));
        Assert.Equal(HttpStatusCode.Unauthorized, deposit.StatusCode);

        var transfer = await client.SendAsync(
            TransferRequest(account.AccountNumber, "00000099", 10m));
        Assert.Equal(HttpStatusCode.Unauthorized, transfer.StatusCode);

        var getAccount = await client.GetAsync($"/api/accounts/by-number/{account.AccountNumber}");
        Assert.Equal(HttpStatusCode.Unauthorized, getAccount.StatusCode);

        var statement = await client.GetAsync(
            $"/api/accounts/by-number/{account.AccountNumber}/transactions");
        Assert.Equal(HttpStatusCode.Unauthorized, statement.StatusCode);
    }

    [Fact]
    public async Task Deposit_With_Same_Idempotency_Key_Should_Apply_Only_Once()
    {
        using var client = CreateClient();
        var account = await CreateAccountAsync(client, "Ana");
        var auth = await LoginAsync(client, account.AccountNumber);
        Authorize(client, auth.Token);

        var key = Guid.NewGuid().ToString();

        var firstResponse = await client.SendAsync(DepositRequest(account.AccountNumber, 25m, key));
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal("application/json", firstResponse.Content.Headers.ContentType!.MediaType);
        var first = (await firstResponse.Content.ReadFromJsonAsync<DepositResponse>(Json))!;

        var replayResponse = await client.SendAsync(DepositRequest(account.AccountNumber, 25m, key));
        Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
        Assert.Equal("application/json", replayResponse.Content.Headers.ContentType!.MediaType);
        var replay = (await replayResponse.Content.ReadFromJsonAsync<DepositResponse>(Json))!;

        Assert.Equal(first.TransactionId, replay.TransactionId);
        Assert.Equal(25m, replay.NewBalance);

        var after = await GetAccountAsync(client, account.AccountNumber);
        Assert.Equal(25m, after.Balance);

        var statement = await GetStatementAsync(client, account.AccountNumber);
        Assert.Equal(1, statement.TotalCount);
    }

    [Fact]
    public async Task Transfer_With_Same_Idempotency_Key_Should_Apply_Only_Once()
    {
        using var client = CreateClient();
        var from = await CreateAccountAsync(client, "Ana", initialBalance: 100m);
        var to = await CreateAccountAsync(client, "Bia");
        var auth = await LoginAsync(client, from.AccountNumber);
        Authorize(client, auth.Token);

        var key = Guid.NewGuid().ToString();

        var firstResponse = await client.SendAsync(
            TransferRequest(from.AccountNumber, to.AccountNumber, 30m, key));
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal("application/json", firstResponse.Content.Headers.ContentType!.MediaType);
        var first = (await firstResponse.Content.ReadFromJsonAsync<TransferResponse>(Json))!;

        var replayResponse = await client.SendAsync(
            TransferRequest(from.AccountNumber, to.AccountNumber, 30m, key));
        Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
        Assert.Equal("application/json", replayResponse.Content.Headers.ContentType!.MediaType);
        var replay = (await replayResponse.Content.ReadFromJsonAsync<TransferResponse>(Json))!;

        Assert.Equal(first.TransferId, replay.TransferId);

        var fromAfter = await GetAccountAsync(client, from.AccountNumber);
        var toAfter = await GetAccountAsAsync(to.AccountNumber);
        Assert.Equal(70m, fromAfter.Balance);
        Assert.Equal(30m, toAfter.Balance);

        var fromStatement = await GetStatementAsync(client, from.AccountNumber);
        Assert.Equal(2, fromStatement.TotalCount);
    }

    [Fact]
    public async Task Deposit_To_Account_That_Is_Not_The_Caller_Should_Return_403()
    {
        using var client = CreateClient();
        var account = await CreateAccountAsync(client, "Ana");
        var auth = await LoginAsync(client, account.AccountNumber);
        Authorize(client, auth.Token);

        var response = await client.SendAsync(DepositRequest("00000099", 10m));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Login_With_Wrong_Password_Should_Return_401()
    {
        using var client = CreateClient();
        var account = await CreateAccountAsync(client, "Ana");

        var response = await client.PostAsJsonAsync(
            "/api/accounts/access",
            new ClientAccessRequest(account.AccountNumber, "senha-errada"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Transfer_With_Insufficient_Balance_Should_Return_400_And_Keep_Balances()
    {
        using var client = CreateClient();
        var from = await CreateAccountAsync(client, "Ana", initialBalance: 10m);
        var to = await CreateAccountAsync(client, "Bia");
        var auth = await LoginAsync(client, from.AccountNumber);
        Authorize(client, auth.Token);

        var response = await client.SendAsync(
            TransferRequest(from.AccountNumber, to.AccountNumber, 50m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Contains("Saldo insuficiente", problem.GetProperty("detail").GetString());

        var fromAfter = await GetAccountAsync(client, from.AccountNumber);
        var toAfter = await GetAccountAsAsync(to.AccountNumber);
        Assert.Equal(10m, fromAfter.Balance);
        Assert.Equal(0m, toAfter.Balance);

        var fromStatement = await GetStatementAsync(client, from.AccountNumber);
        Assert.Equal(1, fromStatement.TotalCount);
    }

    [Fact]
    public async Task CreateAccount_With_Invalid_Payload_Should_Return_400_With_Errors()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/accounts",
            new CreateAccountRequest("", "123", "456"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal(400, problem.GetProperty("status").GetInt32());

        var errors = problem.GetProperty("errors");
        // O MVC também aplica camelCase nas chaves do dicionário de erros.
        Assert.True(errors.TryGetProperty("holderName", out _));
        Assert.True(errors.TryGetProperty("password", out _));
        Assert.True(errors.TryGetProperty("confirmPassword", out _));
    }
}
