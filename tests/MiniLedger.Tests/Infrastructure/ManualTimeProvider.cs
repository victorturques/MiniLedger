namespace MiniLedger.Tests.Infrastructure;

/// <summary>
/// TimeProvider controlável para testar expiração sem esperar tempo real.
/// </summary>
public sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public ManualTimeProvider(DateTimeOffset now) => _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan delta) => _now += delta;
}
