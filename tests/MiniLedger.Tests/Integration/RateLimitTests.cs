using System.Net;
using System.Net.Http.Json;
using MiniLedger.Application.DTOs;
using MiniLedger.Tests.Infrastructure;

namespace MiniLedger.Tests.Integration;

/// <summary>
/// Exercita as regras reais de rate limiting (5 requisições/min por IP no login),
/// usando uma factory que não desliga o IpRateLimiting como a suíte E2E padrão.
/// </summary>
public class RateLimitTests : IDisposable
{
    private readonly MiniLedgerWebAppFactory _factory = new(enableRateLimiting: true);

    public void Dispose() => _factory.Dispose();

    private HttpClient CreateClient(string? clientId = null)
    {
        var client = _factory.CreateClient();

        if (clientId is not null)
            client.DefaultRequestHeaders.Add("X-ClientId", clientId);

        return client;
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client)
        => client.PostAsJsonAsync(
            "/api/accounts/access",
            new ClientAccessRequest("00000001", "senha-errada"));

    [Fact]
    public async Task Login_Should_Return_429_After_Five_Attempts_In_A_Minute()
    {
        using var client = CreateClient();

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var allowed = await LoginAsync(client);
            Assert.Equal(HttpStatusCode.Unauthorized, allowed.StatusCode);
        }

        var blocked = await LoginAsync(client);

        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
    }

    [Fact]
    public async Task Rate_Limit_Should_Be_Shared_By_Client_Ids_On_The_Same_Ip()
    {
        using var firstClient = CreateClient($"cliente-a-{Guid.NewGuid():N}");
        for (var attempt = 1; attempt <= 5; attempt++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(firstClient)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await LoginAsync(firstClient)).StatusCode);

        // O IpCounterKeyBuilder monta a chave do contador apenas com IP + período,
        // então o X-ClientId não abre uma cota separada: mesmo IP, mesma cota.
        using var secondClient = CreateClient($"cliente-b-{Guid.NewGuid():N}");
        var response = await LoginAsync(secondClient);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Fact]
    public async Task Health_Endpoint_Should_Not_Be_Rate_Limited()
    {
        using var client = CreateClient();
        for (var attempt = 1; attempt <= 5; attempt++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await LoginAsync(client)).StatusCode);

        var health = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }
}
