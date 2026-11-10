using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using MiniLedger.Application.DTOs;
using MiniLedger.Tests.Infrastructure;

namespace MiniLedger.Tests.Integration;

/// <summary>
/// Cobre o contrato de idempotência: replay só quando a requisição é idêntica,
/// conflito quando a mesma chave é reutilizada com outro payload e ausência de
/// cache para erros de validação (o cliente pode corrigir o corpo e tentar de novo).
/// </summary>
public class IdempotencyTests : IClassFixture<MiniLedgerWebAppFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly MiniLedgerWebAppFactory _factory;

    public IdempotencyTests(MiniLedgerWebAppFactory factory) => _factory = factory;

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

    private static async Task<AuthResponse> LoginAsync(HttpClient client, string accountNumber)
    {
        var response = await client.PostAsJsonAsync(
            "/api/accounts/access",
            new ClientAccessRequest(accountNumber, "1234"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
    }

    private static void Authorize(HttpClient client, string token)
        => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static HttpRequestMessage DepositRequest(string accountNumber, decimal amount, string key)
        => new(HttpMethod.Post, "/api/accounts/deposit")
        {
            Content = JsonContent.Create(new DepositRequest(accountNumber, amount)),
            Headers = { { "Idempotency-Key", key } }
        };

    private static HttpRequestMessage TransferRequest(
        string fromAccountNumber,
        string toAccountNumber,
        decimal amount,
        string key)
        => new(HttpMethod.Post, "/api/transfers")
        {
            Content = JsonContent.Create(new TransferRequest(fromAccountNumber, toAccountNumber, amount)),
            Headers = { { "Idempotency-Key", key } }
        };

    private async Task<AccountResponse> GetBalanceAsAsync(string accountNumber)
    {
        using var client = CreateClient();
        var auth = await LoginAsync(client, accountNumber);
        Authorize(client, auth.Token);

        var response = await client.GetAsync($"/api/accounts/by-number/{accountNumber}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AccountResponse>(Json))!;
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, int expectedStatus)
    {
        Assert.Equal((HttpStatusCode)expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal(expectedStatus, problem.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Deposit_With_Same_Key_And_Different_Payload_Should_Return_409()
    {
        using var client = CreateClient();
        var account = await CreateAccountAsync(client, "Ana");
        var auth = await LoginAsync(client, account.AccountNumber);
        Authorize(client, auth.Token);

        var key = Guid.NewGuid().ToString();

        var firstResponse = await client.SendAsync(DepositRequest(account.AccountNumber, 10m, key));
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        var first = (await firstResponse.Content.ReadFromJsonAsync<DepositResponse>(Json))!;

        var conflicting = await client.SendAsync(DepositRequest(account.AccountNumber, 20m, key));
        await AssertProblemAsync(conflicting, StatusCodes.Status409Conflict);

        var balanceAfterConflict = await GetBalanceAsAsync(account.AccountNumber);
        Assert.Equal(10m, balanceAfterConflict.Balance);

        // O conflito não pode sobrescrever a entrada original: o replay válido continua funcionando.
        var replayResponse = await client.SendAsync(DepositRequest(account.AccountNumber, 10m, key));
        Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
        var replay = (await replayResponse.Content.ReadFromJsonAsync<DepositResponse>(Json))!;
        Assert.Equal(first.TransactionId, replay.TransactionId);

        var finalBalance = await GetBalanceAsAsync(account.AccountNumber);
        Assert.Equal(10m, finalBalance.Balance);
    }

    [Fact]
    public async Task Transfer_With_Same_Key_And_Different_Payload_Should_Return_409()
    {
        using var client = CreateClient();
        var from = await CreateAccountAsync(client, "Ana", initialBalance: 100m);
        var firstTarget = await CreateAccountAsync(client, "Bia");
        var secondTarget = await CreateAccountAsync(client, "Caio");

        var auth = await LoginAsync(client, from.AccountNumber);
        Authorize(client, auth.Token);

        var key = Guid.NewGuid().ToString();

        var firstResponse = await client.SendAsync(
            TransferRequest(from.AccountNumber, firstTarget.AccountNumber, 10m, key));
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        var first = (await firstResponse.Content.ReadFromJsonAsync<TransferResponse>(Json))!;

        var conflicting = await client.SendAsync(
            TransferRequest(from.AccountNumber, secondTarget.AccountNumber, 10m, key));
        await AssertProblemAsync(conflicting, StatusCodes.Status409Conflict);

        Assert.Equal(90m, (await GetBalanceAsAsync(from.AccountNumber)).Balance);
        Assert.Equal(10m, (await GetBalanceAsAsync(firstTarget.AccountNumber)).Balance);
        Assert.Equal(0m, (await GetBalanceAsAsync(secondTarget.AccountNumber)).Balance);

        var replayResponse = await client.SendAsync(
            TransferRequest(from.AccountNumber, firstTarget.AccountNumber, 10m, key));
        Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
        var replay = (await replayResponse.Content.ReadFromJsonAsync<TransferResponse>(Json))!;
        Assert.Equal(first.TransferId, replay.TransferId);

        Assert.Equal(90m, (await GetBalanceAsAsync(from.AccountNumber)).Balance);
        Assert.Equal(10m, (await GetBalanceAsAsync(firstTarget.AccountNumber)).Balance);
    }

    [Fact]
    public async Task Deposit_With_Same_Key_After_Validation_Error_Should_Not_Replay_The_400()
    {
        using var client = CreateClient();
        var account = await CreateAccountAsync(client, "Ana");
        var auth = await LoginAsync(client, account.AccountNumber);
        Authorize(client, auth.Token);

        var key = Guid.NewGuid().ToString();

        var invalid = await client.SendAsync(DepositRequest(account.AccountNumber, 0m, key));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var corrected = await client.SendAsync(DepositRequest(account.AccountNumber, 10m, key));
        Assert.Equal(HttpStatusCode.OK, corrected.StatusCode);

        var balance = await GetBalanceAsAsync(account.AccountNumber);
        Assert.Equal(10m, balance.Balance);
    }

    [Fact]
    public async Task Transfer_With_Same_Key_After_Validation_Error_Should_Not_Replay_The_400()
    {
        using var client = CreateClient();
        var from = await CreateAccountAsync(client, "Ana", initialBalance: 100m);
        var to = await CreateAccountAsync(client, "Bia");

        var auth = await LoginAsync(client, from.AccountNumber);
        Authorize(client, auth.Token);

        var key = Guid.NewGuid().ToString();

        var invalid = await client.SendAsync(
            TransferRequest(from.AccountNumber, to.AccountNumber, 0m, key));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var corrected = await client.SendAsync(
            TransferRequest(from.AccountNumber, to.AccountNumber, 10m, key));
        Assert.Equal(HttpStatusCode.OK, corrected.StatusCode);

        Assert.Equal(90m, (await GetBalanceAsAsync(from.AccountNumber)).Balance);
        Assert.Equal(10m, (await GetBalanceAsAsync(to.AccountNumber)).Balance);
    }

    [Fact]
    public async Task Identical_Request_Should_Still_Replay_Cached_Response()
    {
        using var client = CreateClient();
        var account = await CreateAccountAsync(client, "Ana");
        var auth = await LoginAsync(client, account.AccountNumber);
        Authorize(client, auth.Token);

        var key = Guid.NewGuid().ToString();

        var firstResponse = await client.SendAsync(DepositRequest(account.AccountNumber, 10m, key));
        var first = (await firstResponse.Content.ReadFromJsonAsync<DepositResponse>(Json))!;

        var replayResponse = await client.SendAsync(DepositRequest(account.AccountNumber, 10m, key));
        var replay = (await replayResponse.Content.ReadFromJsonAsync<DepositResponse>(Json))!;

        Assert.Equal(first.TransactionId, replay.TransactionId);
        Assert.Equal(first.NewBalance, replay.NewBalance);
        Assert.Equal(10m, (await GetBalanceAsAsync(account.AccountNumber)).Balance);
    }
}
