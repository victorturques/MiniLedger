using MiniLedger.Application.DTOs;
using MiniLedger.Application.Interfaces;

namespace MiniLedger.Tests.Infrastructure;

/// <summary>
/// Substitui o TokenService real nos testes de unidade para que não seja necessário
/// configurar JWT (chave, issuer, audience) e para permitir verificar o que foi passado.
/// </summary>
public sealed class FakeTokenService : ITokenService
{
    public const string Token = "token-de-teste";

    public int Calls { get; private set; }
    public AccountResponse? LastAccount { get; private set; }

    public string GenerateToken(AccountResponse account)
    {
        Calls++;
        LastAccount = account;
        return Token;
    }
}
