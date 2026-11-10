using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MiniLedger.Api.Middleware;

namespace MiniLedger.Tests.Middleware;

public class ExceptionMiddlewareTests
{
    private const string RequestPath = "/api/accounts/00000001";

    private static async Task<(HttpContext Context, JsonElement Problem)> InvokeAsync(
        Exception exception,
        string path = RequestPath)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();

        var middleware = new ExceptionMiddleware(
            _ => throw exception,
            NullLogger<ExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var problem = JsonSerializer.Deserialize<JsonElement>(await reader.ReadToEndAsync());

        return (context, problem);
    }

    [Fact]
    public async Task ArgumentException_Should_Map_To_400()
    {
        var (context, problem) = await InvokeAsync(new ArgumentException("valor inválido"));

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal(400, problem.GetProperty("status").GetInt32());
        Assert.Equal("Requisição inválida.", problem.GetProperty("title").GetString());
        Assert.Equal("valor inválido", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task ArgumentNullException_Should_Map_To_400()
    {
        var (context, problem) = await InvokeAsync(new ArgumentNullException("request"));

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal(400, problem.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task KeyNotFoundException_Should_Map_To_404()
    {
        var (context, problem) = await InvokeAsync(new KeyNotFoundException("conta não encontrada"));

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.Equal(404, problem.GetProperty("status").GetInt32());
        Assert.Equal("Recurso não encontrado.", problem.GetProperty("title").GetString());
        Assert.Equal("conta não encontrada", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task UnauthorizedAccessException_Should_Map_To_401()
    {
        var (context, problem) = await InvokeAsync(new UnauthorizedAccessException("sem permissão"));

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Equal(401, problem.GetProperty("status").GetInt32());
        Assert.Equal("Não autorizado.", problem.GetProperty("title").GetString());
        Assert.Equal("sem permissão", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Generic_Exception_Should_Map_To_500_Without_Leaking_Message()
    {
        const string secret = "detalhe interno secreto: senha=1234";
        var (context, problem) = await InvokeAsync(new InvalidOperationException(secret));

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal(500, problem.GetProperty("status").GetInt32());
        Assert.Equal("Ocorreu um erro interno no servidor.", problem.GetProperty("title").GetString());
        Assert.Equal(
            "Ocorreu um erro inesperado. Tente novamente mais tarde.",
            problem.GetProperty("detail").GetString());

        var body = problem.GetRawText();
        Assert.DoesNotContain(secret, body);
        Assert.DoesNotContain(nameof(InvalidOperationException), body);
        Assert.DoesNotContain("StackTrace", body);
    }

    [Fact]
    public async Task All_Handled_Exceptions_Should_Return_Problem_Json_Content_Type()
    {
        Exception[] exceptions =
        [
            new ArgumentException("400"),
            new KeyNotFoundException("404"),
            new UnauthorizedAccessException("401"),
            new InvalidOperationException("500")
        ];

        foreach (var exception in exceptions)
        {
            var (context, _) = await InvokeAsync(exception);
            Assert.Equal("application/problem+json", context.Response.ContentType);
        }
    }

    [Fact]
    public async Task ProblemDetails_Instance_Should_Be_The_Request_Path()
    {
        const string path = "/api/transfers";
        var (_, problem) = await InvokeAsync(new ArgumentException("x"), path);

        Assert.Equal(path, problem.GetProperty("instance").GetString());
    }

    [Fact]
    public async Task Response_Should_Be_CamelCase_Json()
    {
        var (context, problem) = await InvokeAsync(new ArgumentException("x"));

        Assert.Equal(JsonValueKind.Object, problem.ValueKind);
        Assert.True(problem.TryGetProperty("status", out _));
        Assert.True(problem.TryGetProperty("title", out _));
        Assert.True(problem.TryGetProperty("detail", out _));
        Assert.True(problem.TryGetProperty("instance", out _));
    }

    [Fact]
    public async Task Should_Not_Alter_Response_When_No_Exception_Is_Thrown()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var called = false;

        var middleware = new ExceptionMiddleware(
            ctx =>
            {
                called = true;
                ctx.Response.StatusCode = StatusCodes.Status204NoContent;
                return Task.CompletedTask;
            },
            NullLogger<ExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.True(called);
        Assert.Equal(StatusCodes.Status204NoContent, context.Response.StatusCode);
        Assert.Null(context.Response.ContentType);
        Assert.Equal(0, context.Response.Body.Length);
    }

    [Fact]
    public async Task UseExceptionMiddleware_Should_Catch_Exceptions_From_Next_Middleware()
    {
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var builder = new ApplicationBuilder(services);
        builder.UseExceptionMiddleware();
        builder.Run(_ => throw new ArgumentException("falhou no pipeline"));

        var app = builder.Build();

        var context = new DefaultHttpContext();
        context.Request.Path = "/health";
        context.Response.Body = new MemoryStream();

        await app(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
    }
}
