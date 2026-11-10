using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MiniLedger.Api.Serialization;

/// <summary>
/// Opções de serialização usadas para gravar e reproduzir respostas idempotentes.
/// Precisam ser iguais às do MVC (camelCase), senão a resposta original e o replay
/// teriam formatos diferentes.
/// </summary>
public static class ApiJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(object value) => JsonSerializer.Serialize(value, Options);

    /// <summary>
    /// Fingerprint estável do payload: identifica se um replay traz o mesmo corpo
    /// que originou a resposta cacheada.
    /// </summary>
    public static string Hash(object value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Serialize(value))));
}
