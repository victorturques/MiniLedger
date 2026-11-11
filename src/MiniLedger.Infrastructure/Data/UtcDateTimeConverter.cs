using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MiniLedger.Infrastructure.Data;

/// <summary>
/// O SQLite grava DateTime como texto sem fuso, então ao ler o valor volta com
/// <see cref="DateTimeKind.Unspecified"/>. O System.Text.Json só escreve o sufixo "Z"
/// quando o Kind é Utc; sem ele o navegador interpreta o horário como local e a data
/// aparece deslocada. Restaurar o Kind na leitura mantém o contrato de que toda data
/// da API é UTC.
/// </summary>
public sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter()
        : base(
            // Na escrita, valores Local viram UTC; Unspecified é assumido como UTC,
            // já que o domínio só produz DateTime.UtcNow.
            v => v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : DateTime.SpecifyKind(v, DateTimeKind.Utc),
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
    {
    }
}
