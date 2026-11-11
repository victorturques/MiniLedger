using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MiniLedger.Application.DTOs;
using MiniLedger.Tests.Infrastructure;

namespace MiniLedger.Tests.Integration;

/// <summary>
/// Garante que toda data que sai da API é marcada como UTC. Sem o sufixo "Z" o
/// navegador trata o horário como local e o extrato aparece deslocado em relação
/// ao relógio de quem está usando.
/// </summary>
public class UtcDateTimeTests : IClassFixture<MiniLedgerWebAppFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Regex UtcMarker = new("\"createdAt\":\"[^\"]+Z\"");

    private readonly MiniLedgerWebAppFactory _factory;

    public UtcDateTimeTests(MiniLedgerWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task CreatedAt_volta_com_Kind_Utc_apos_round_trip_no_sqlite()
    {
        await using var db = new SqliteTestDb();
        var account = await db.SeedAccountAsync("10000001");

        // Limpa o ChangeTracker para forçar a releitura do banco em vez de
        // reaproveitar a instância em memória, que já está com Kind = Utc.
        db.Context.ChangeTracker.Clear();

        var reloaded = await db.Context.Accounts.SingleAsync(a => a.Id == account.Id);

        Assert.Equal(DateTimeKind.Utc, reloaded.CreatedAt.Kind);
        Assert.Equal(account.CreatedAt, reloaded.CreatedAt);
    }

    [Fact]
    public async Task Datas_da_api_saem_com_sufixo_Z()
    {
        var client = _factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync(
            "/api/accounts",
            new CreateAccountRequest("Fuso Horario", "1234", "1234"));
        var account = (await createResponse.Content.ReadFromJsonAsync<AccountResponse>(Json))!;

        var loginResponse = await client.PostAsJsonAsync(
            "/api/accounts/access",
            new ClientAccessRequest(account.AccountNumber, "1234"));
        var auth = (await loginResponse.Content.ReadFromJsonAsync<AuthResponse>(Json))!;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        await client.PostAsJsonAsync(
            "/api/accounts/deposit",
            new DepositRequest(account.AccountNumber, 10m));

        var accountJson = await client.GetStringAsync($"/api/accounts/by-number/{account.AccountNumber}");
        var statementJson = await client.GetStringAsync($"/api/accounts/by-number/{account.AccountNumber}/transactions");

        Assert.Matches(UtcMarker, accountJson);
        Assert.Matches(UtcMarker, statementJson);
    }
}
