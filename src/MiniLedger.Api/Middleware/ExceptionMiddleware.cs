using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace MiniLedger.Api.Middleware;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ocorreu uma exceção não tratada.");
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/problem+json";

        var problemDetails = new ProblemDetails
        {
            Status = (int)HttpStatusCode.InternalServerError,
            Title = "Ocorreu um erro interno no servidor.",
            Detail = "Ocorreu um erro inesperado. Tente novamente mais tarde.",
            Instance = context.Request.Path
        };

        if (exception is ArgumentException argEx)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            problemDetails.Status = (int)HttpStatusCode.BadRequest;
            problemDetails.Title = "Requisição inválida.";
            problemDetails.Detail = argEx.Message;
        }
        else if (exception is KeyNotFoundException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            problemDetails.Status = (int)HttpStatusCode.NotFound;
            problemDetails.Title = "Recurso não encontrado.";
            problemDetails.Detail = exception.Message;
        }
        else if (exception is UnauthorizedAccessException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            problemDetails.Status = (int)HttpStatusCode.Unauthorized;
            problemDetails.Title = "Não autorizado.";
            problemDetails.Detail = exception.Message;
        }
        else
        {
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
        }

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var json = JsonSerializer.Serialize(problemDetails, options);
        await context.Response.WriteAsync(json);
    }
}

public static class ExceptionMiddlewareExtensions
{
    public static IApplicationBuilder UseExceptionMiddleware(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ExceptionMiddleware>();
    }
}
