using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MiniLedger.Application.DTOs;
using MiniLedger.Tests.Infrastructure;

namespace MiniLedger.Tests.Integration;

/// <summary>
/// Um cliente autenticado só pode ler e movimentar a própria conta.
/// Estes testes cobrem acesso cruzado (IDOR) entre duas contas reais.
/// </summary>
public class AuthorizationTests : IClassFixture<MiniLedgerWebAppFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly MiniLedgerWebAppFactory _factory;

    public AuthorizationTests(MiniLedgerWebAppFactory factory) => _factory = factory;

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

    private static HttpRequestMessage DepositRequest(string accountNumber, decimal amount)
        => new(HttpMethod.Post, "/api/accounts/deposit")
        {
            Content = JsonContent.Create(new DepositRequest(accountNumber, amount))
        };

    private static HttpRequestMessage TransferRequest(
        string fromAccountNumber,
        string toAccountNumber,
        decimal amount)
        => new(HttpMethod.Post, "/api/transfers")
        {
            Content = JsonContent.Create(new TransferRequest(fromAccountNumber, toAccountNumber, amount))
        };

    private static async Task<AccountResponse> GetAccountAsync(HttpClient client, string accountNumber)
    {
        var response = await client.GetAsync($"/api/accounts/by-number/{accountNumber}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AccountResponse>(Json))!;
    }

    [Fact]
    public async Task Owner_Should_Read_Own_Account_And_Statement()
    {
        using var client = CreateClient();
        var account = await CreateAccountAsync(client, "Ana", initialBalance: 100m);
        var auth = await LoginAsync(client, account.AccountNumber);
        Authorize(client, auth.Token);

        var accountResponse = await client.GetAsync($"/api/accounts/by-number/{account.AccountNumber}");
        Assert.Equal(HttpStatusCode.OK, accountResponse.StatusCode);

        var statementResponse = await client.GetAsync(
            $"/api/accounts/by-number/{account.AccountNumber}/transactions");
        Assert.Equal(HttpStatusCode.OK, statementResponse.StatusCode);
    }

    [Fact]
    public async Task Other_User_Should_Not_Read_Account_By_Number()
    {
        using var client = CreateClient();
        var alice = await CreateAccountAsync(client, "Alice", initialBalance: 100m);
        var bob = await CreateAccountAsync(client, "Bob", initialBalance: 50m);

        var auth = await LoginAsync(client, alice.AccountNumber);
        Authorize(client, auth.Token);

        var response = await client.GetAsync($"/api/accounts/by-number/{bob.AccountNumber}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Other_User_Should_Not_Read_Statement()
    {
        using var client = CreateClient();
        var alice = await CreateAccountAsync(client, "Alice", initialBalance: 100m);
        var bob = await CreateAccountAsync(client, "Bob", initialBalance: 50m);

        var auth = await LoginAsync(client, alice.AccountNumber);
        Authorize(client, auth.Token);

        var response = await client.GetAsync(
            $"/api/accounts/by-number/{bob.AccountNumber}/transactions");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Other_User_Should_Not_Deposit_Into_Account()
    {
        using var client = CreateClient();
        var alice = await CreateAccountAsync(client, "Alice", initialBalance: 100m);
        var bob = await CreateAccountAsync(client, "Bob", initialBalance: 50m);

        var auth = await LoginAsync(client, alice.AccountNumber);
        Authorize(client, auth.Token);

        var response = await client.SendAsync(DepositRequest(bob.AccountNumber, 10m));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var bobClient = CreateClient();
        var bobAuth = await LoginAsync(bobClient, bob.AccountNumber);
        Authorize(bobClient, bobAuth.Token);
        var bobAfter = await GetAccountAsync(bobClient, bob.AccountNumber);
        Assert.Equal(50m, bobAfter.Balance);
    }

    [Fact]
    public async Task Other_User_Should_Not_Transfer_From_Account()
    {
        using var setupClient = CreateClient();
        var alice = await CreateAccountAsync(setupClient, "Alice", initialBalance: 100m);
        var bob = await CreateAccountAsync(setupClient, "Bob", initialBalance: 50m);

        using var aliceClient = CreateClient();
        var aliceAuth = await LoginAsync(aliceClient, alice.AccountNumber);
        Authorize(aliceClient, aliceAuth.Token);

        using var bobClient = CreateClient();
        var bobAuth = await LoginAsync(bobClient, bob.AccountNumber);
        Authorize(bobClient, bobAuth.Token);

        var response = await bobClient.SendAsync(
            TransferRequest(alice.AccountNumber, bob.AccountNumber, 10m));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var aliceAfter = await GetAccountAsync(aliceClient, alice.AccountNumber);
        var bobAfter = await GetAccountAsync(bobClient, bob.AccountNumber);
        Assert.Equal(100m, aliceAfter.Balance);
        Assert.Equal(50m, bobAfter.Balance);
    }
}
