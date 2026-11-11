using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.EntityFrameworkCore;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using AspNetCoreRateLimit;
using MiniLedger.Api.Middleware;
using MiniLedger.Application.DTOs;
using MiniLedger.Application.Interfaces;
using MiniLedger.Application.Services;
using MiniLedger.Application.Validators;
using MiniLedger.Infrastructure.Data;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Em produção (IIS/Monster) o conteúdo fica em wwwroot; a pasta "private"
// ao lado é gravável e sobrevive aos deploys.
var privateFolder = Path.GetFullPath(
    Path.Combine(builder.Environment.ContentRootPath, "..", "private"));

var logFolder = builder.Environment.IsProduction()
    ? Path.Combine(privateFolder, "logs")
    : Path.Combine(builder.Environment.ContentRootPath, "logs");

Directory.CreateDirectory(logFolder);

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(logFolder, "miniledger-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7)
    .CreateLogger();

builder.Host.UseSerilog();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DictionaryKeyPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.OpenApiInfo { Title = "MiniLedger API", Version = "v1" });
});
builder.Services.AddHealthChecks();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(sp => new IdempotencyStore(
    sp.GetRequiredService<TimeProvider>(),
    IdempotencyStore.DefaultTtl));
builder.Services.AddScoped<ITokenService, TokenService>();

builder.Services.AddMemoryCache();
builder.Services.Configure<IpRateLimitOptions>(builder.Configuration.GetSection("IpRateLimiting"));
builder.Services.AddInMemoryRateLimiting();
builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();

var jwtSection = builder.Configuration.GetSection("Jwt");
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSection["Issuer"],
        ValidAudience = jwtSection["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSection["Key"]!))
    };
});
builder.Services.AddAuthorization();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")!;

if (connectionString.Contains("{PrivateFolder}", StringComparison.OrdinalIgnoreCase))
{
    Directory.CreateDirectory(privateFolder);
    connectionString = connectionString.Replace(
        "{PrivateFolder}", privateFolder, StringComparison.OrdinalIgnoreCase);
}

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(connectionString));

builder.Services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
builder.Services.AddScoped<DbContext>(sp => sp.GetRequiredService<AppDbContext>());
builder.Services.AddScoped<IAccountService, AccountService>();
builder.Services.AddScoped<ITransferService, TransferService>();

builder.Services.AddScoped<IValidator<TransferRequest>, TransferRequestValidator>();
builder.Services.AddScoped<IValidator<CreateAccountRequest>, CreateAccountRequestValidator>();
builder.Services.AddScoped<IValidator<DepositRequest>, DepositRequestValidator>();
builder.Services.AddScoped<IValidator<ClientAccessRequest>, ClientAccessRequestValidator>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

app.UseExceptionMiddleware();

app.UseSerilogRequestLogging();

// Antes dos arquivos estáticos: senão o HTML e o JS são servidos em HTTP enquanto
// a API redireciona para HTTPS, e o navegador bloqueia a troca de origem (CORS).
app.UseHttpsRedirection();

app.UseIpRateLimiting();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "MiniLedger API v1");
    c.RoutePrefix = "swagger";
});

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // Sem isso o navegador pode manter index.html/app.js antigos após um deploy
        // e continuar rodando uma versão que já foi corrigida.
        ctx.Context.Response.Headers["Cache-Control"] = "no-cache, must-revalidate";
    }
});

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

// Necessário para que WebApplicationFactory<Program> encontre o entry point
// em projetos com top-level statements.
public partial class Program;
