using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using MiniLedger.Application.DTOs;
using MiniLedger.Application.Services;

namespace MiniLedger.Tests.Services;

public class TokenServiceTests
{
    private const string Key = "MINILEDGER_SUPER_SECRET_KEY_FOR_TESTS_1234567890";
    private const string Issuer = "MiniLedger.Api";
    private const string Audience = "MiniLedger.Client";

    private static readonly AccountResponse Account = new(
        Guid.NewGuid(),
        "Ana Silva",
        "00000001",
        100m,
        new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));

    private static TokenService CreateService(string? expireMinutes = "60")
    {
        var settings = new Dictionary<string, string?>
        {
            ["Jwt:Key"] = Key,
            ["Jwt:Issuer"] = Issuer,
            ["Jwt:Audience"] = Audience,
            ["Jwt:ExpireMinutes"] = expireMinutes
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        return new TokenService(configuration);
    }

    private static JwtSecurityToken ReadToken(string token)
        => new JwtSecurityTokenHandler().ReadJwtToken(token);

    private static TokenValidationParameters ValidationParameters(string key = Key) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = Issuer,
        ValidateAudience = true,
        ValidAudience = Audience,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
        ClockSkew = TimeSpan.Zero
    };

    [Fact]
    public void GenerateToken_Should_Return_Three_Part_Jwt()
    {
        var token = CreateService().GenerateToken(Account);

        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.Equal(3, token.Split('.').Length);
    }

    [Fact]
    public void GenerateToken_Should_Include_Account_Claims()
    {
        var token = CreateService().GenerateToken(Account);
        var jwt = ReadToken(token);

        Assert.Equal(Account.Id.ToString(), jwt.Claims.Single(c => c.Type == ClaimTypes.NameIdentifier).Value);
        Assert.Equal(Account.HolderName, jwt.Claims.Single(c => c.Type == ClaimTypes.Name).Value);
        Assert.Equal(Account.AccountNumber, jwt.Claims.Single(c => c.Type == "AccountNumber").Value);
    }

    [Fact]
    public void GenerateToken_Should_Use_Configured_Issuer_And_Audience()
    {
        var jwt = ReadToken(CreateService().GenerateToken(Account));

        Assert.Equal(Issuer, jwt.Issuer);
        Assert.Equal(new[] { Audience }, jwt.Audiences);
    }

    [Fact]
    public void GenerateToken_Should_Expire_In_Sixty_Minutes_By_Default()
    {
        var jwt = ReadToken(CreateService().GenerateToken(Account));

        Assert.InRange(jwt.ValidTo, DateTime.UtcNow.AddMinutes(59), DateTime.UtcNow.AddMinutes(61));
    }

    [Fact]
    public void GenerateToken_Should_Expire_According_To_Configured_Minutes()
    {
        var jwt = ReadToken(CreateService("5").GenerateToken(Account));

        Assert.InRange(jwt.ValidTo, DateTime.UtcNow.AddMinutes(4), DateTime.UtcNow.AddMinutes(6));
    }

    [Fact]
    public void GenerateToken_Should_Produce_Valid_Signature()
    {
        var token = CreateService().GenerateToken(Account);
        var handler = new JwtSecurityTokenHandler();

        var principal = handler.ValidateToken(token, ValidationParameters(), out var validated);

        Assert.IsType<JwtSecurityToken>(validated);
        Assert.Equal(Account.AccountNumber, principal.FindFirst("AccountNumber")!.Value);
        Assert.Equal(Account.HolderName, principal.FindFirst(ClaimTypes.Name)!.Value);
    }

    [Fact]
    public void GenerateToken_Should_Not_Validate_When_Signature_Is_Tampered()
    {
        var token = CreateService().GenerateToken(Account);
        var parts = token.Split('.');
        var tampered = $"{parts[0]}.{parts[1]}.{new string('A', parts[2].Length)}";
        var handler = new JwtSecurityTokenHandler();

        Assert.ThrowsAny<SecurityTokenException>(
            () => handler.ValidateToken(tampered, ValidationParameters(), out _));
    }

    [Fact]
    public void GenerateToken_Should_Not_Validate_With_Another_Key()
    {
        var token = CreateService().GenerateToken(Account);
        var handler = new JwtSecurityTokenHandler();

        Assert.ThrowsAny<SecurityTokenException>(
            () => handler.ValidateToken(token, ValidationParameters(Key + "-outra"), out _));
    }

    [Fact]
    public void GenerateToken_Should_Not_Validate_When_Expired()
    {
        var token = CreateService("-1").GenerateToken(Account);
        var handler = new JwtSecurityTokenHandler();

        Assert.ThrowsAny<SecurityTokenException>(
            () => handler.ValidateToken(token, ValidationParameters(), out _));
    }

    [Fact]
    public void GenerateToken_Should_Throw_When_ExpireMinutes_Is_Not_A_Number()
    {
        var service = CreateService("abc");

        Assert.Throws<FormatException>(() => service.GenerateToken(Account));
    }
}
