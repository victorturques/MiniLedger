using AspNetCoreRateLimit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MiniLedger.Application.Interfaces;
using MiniLedger.Infrastructure.Data;

namespace MiniLedger.Tests.Infrastructure;

/// <summary>
/// Sobe a API real em memória (TestServer) usando um arquivo SQLite temporário,
/// com as migrations reais aplicadas. Assim o teste E2E exercita o mesmo pipeline
/// HTTP, DI, autenticação e banco da aplicação — mas sem tocar no miniledger.db.
/// </summary>
public sealed class MiniLedgerWebAppFactory : WebApplicationFactory<Program>
{
    private readonly bool _enableRateLimiting;
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"miniledger-e2e-{Guid.NewGuid():N}.db");

    /// <summary>
    /// Construtor sem parâmetros exigido pelo xUnit para usar a factory como class fixture.
    /// </summary>
    public MiniLedgerWebAppFactory() : this(enableRateLimiting: false)
    {
    }

    /// <param name="enableRateLimiting">
    /// A suíte E2E padrão desliga o rate limiting para não interferir nos fluxos.
    /// Testes específicos de limitação passam <c>true</c> para exercitar as regras reais.
    /// </param>
    /// <remarks>
    /// Interno porque o xUnit exige exatamente um construtor público para usar a
    /// factory como class fixture; os testes de rate limiting vivem no mesmo assembly.
    /// </remarks>
    internal MiniLedgerWebAppFactory(bool enableRateLimiting)
    {
        _enableRateLimiting = enableRateLimiting;

        using var context = CreateDbContext();
        context.Database.Migrate();
    }

    private AppDbContext CreateDbContext()
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_databasePath}")
            .Options);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureTestServices(services =>
        {
            // Substitui o banco configurado no Program.cs pelo banco temporário do teste.
            services.RemoveAll<AppDbContext>();
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IAppDbContext>();
            services.RemoveAll<DbContext>();

            services.AddDbContext<AppDbContext>(options => options.UseSqlite($"Data Source={_databasePath}"));
            services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
            services.AddScoped<DbContext>(sp => sp.GetRequiredService<AppDbContext>());

            // O rate limiting de login (5/min) não deve interferir na suíte E2E padrão.
            if (!_enableRateLimiting)
            {
                services.PostConfigure<IpRateLimitOptions>(options =>
                {
                    options.EnableEndpointRateLimiting = false;
                    options.GeneralRules.Clear();
                });
            }
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
            return;

        // Libera os handles do SQLite antes de apagar o arquivo temporário.
        SqliteConnection.ClearAllPools();
        TryDelete(_databasePath);
        TryDelete(_databasePath + "-wal");
        TryDelete(_databasePath + "-shm");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // Arquivo temporário: se não puder ser removido agora, o SO limpa depois.
        }
    }
}
