using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using MiniLedger.Tests.Infrastructure;

namespace MiniLedger.Tests.Integration;

/// <summary>
/// Exercita o pipeline HTTP real: endpoints de infraestrutura (health e arquivos
/// estáticos) e a validação de tokens feita pelo middleware de autenticação.
/// </summary>
public class PipelineTests : IClassFixture<MiniLedgerWebAppFactory>
{
    private const string ProtectedPath = "/api/accounts/by-number/00000001";

    private readonly MiniLedgerWebAppFactory _factory;

    public PipelineTests(MiniLedgerWebAppFactory factory) => _factory = factory;

    private HttpClient CreateClient()
    {
        return _factory.CreateClient();
    }

    private IConfigurationSection JwtSettings
        => _factory.Services.GetRequiredService<IConfiguration>().GetSection("Jwt");

    private string CreateToken(
        DateTime? expires = null,
        DateTime? notBefore = null,
        string? key = null,
        string? issuer = null,
        string? audience = null)
    {
        var jwt = JwtSettings;
        var signingKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(key ?? jwt["Key"]!));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: issuer ?? jwt["Issuer"],
            audience: audience ?? jwt["Audience"],
            claims:
            [
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Name, "Ana Silva"),
                new Claim("AccountNumber", "00000001")
            ],
            notBefore: notBefore ?? DateTime.UtcNow.AddMinutes(-1),
            expires: expires ?? DateTime.UtcNow.AddMinutes(10),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static void Authorize(HttpClient client, string token)
        => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    [Fact]
    public async Task Health_Should_Return_200_Without_Authentication()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Root_Should_Serve_Index_Html()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("<title>MiniLedger</title>", html);
    }

    [Fact]
    public async Task Static_Files_Should_Be_Served()
    {
        using var client = CreateClient();

        var css = await client.GetAsync("/css/styles.css");
        Assert.Equal(HttpStatusCode.OK, css.StatusCode);
        Assert.Equal("text/css", css.Content.Headers.ContentType?.MediaType);

        var js = await client.GetAsync("/js/app.js");
        Assert.Equal(HttpStatusCode.OK, js.StatusCode);
        Assert.Contains("javascript", js.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Static_Files_Should_Not_Be_Cached_Without_Revalidation()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/js/app.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoCache);
    }

    [Fact]
    public async Task Missing_Static_File_Should_Return_404()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/css/nao-existe.css");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Valid_Token_Should_Be_Accepted()
    {
        using var client = CreateClient();
        Authorize(client, CreateToken());

        var response = await client.GetAsync(ProtectedPath);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Expired_Token_Should_Return_401()
    {
        using var client = CreateClient();
        Authorize(client, CreateToken(
            expires: DateTime.UtcNow.AddMinutes(-5),
            notBefore: DateTime.UtcNow.AddMinutes(-10)));

        var response = await client.GetAsync(ProtectedPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Token_Signed_With_Another_Key_Should_Return_401()
    {
        using var client = CreateClient();
        Authorize(client, CreateToken(key: JwtSettings["Key"] + "-outra-chave"));

        var response = await client.GetAsync(ProtectedPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Token_With_Wrong_Issuer_Should_Return_401()
    {
        using var client = CreateClient();
        Authorize(client, CreateToken(issuer: "OutroEmissor"));

        var response = await client.GetAsync(ProtectedPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Token_With_Wrong_Audience_Should_Return_401()
    {
        using var client = CreateClient();
        Authorize(client, CreateToken(audience: "OutroCliente"));

        var response = await client.GetAsync(ProtectedPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Tampered_Token_Should_Return_401()
    {
        using var client = CreateClient();
        var parts = CreateToken().Split('.');
        var tampered = $"{parts[0]}.{parts[1]}.{new string('A', parts[2].Length)}";
        Authorize(client, tampered);

        var response = await client.GetAsync(ProtectedPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("isto-nao-e-um-jwt")]
    [InlineData("aaa.bbb.ccc")]
    [InlineData("")]
    public async Task Malformed_Token_Should_Return_401(string token)
    {
        using var client = CreateClient();
        Authorize(client, token);

        var response = await client.GetAsync(ProtectedPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
