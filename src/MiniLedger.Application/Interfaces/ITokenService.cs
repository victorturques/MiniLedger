using MiniLedger.Application.DTOs;

namespace MiniLedger.Application.Interfaces;

public interface ITokenService
{
    string GenerateToken(AccountResponse account);
}
