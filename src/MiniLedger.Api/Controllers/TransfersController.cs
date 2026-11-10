using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MiniLedger.Api.Serialization;
using MiniLedger.Application.DTOs;
using MiniLedger.Application.Interfaces;
using MiniLedger.Application.Services;

namespace MiniLedger.Api.Controllers;

[ApiController]
[Route("api/transfers")]
[Authorize]
public class TransfersController : ControllerBase
{
    private readonly ITransferService _transferService;
    private readonly IValidator<TransferRequest> _validator;
    private readonly IdempotencyStore _idempotencyStore;

    public TransfersController(ITransferService transferService, IValidator<TransferRequest> validator, IdempotencyStore idempotencyStore)
    {
        _transferService = transferService;
        _validator = validator;
        _idempotencyStore = idempotencyStore;
    }

    [HttpPost]
    public async Task<IActionResult> Transfer([FromBody] TransferRequest request, CancellationToken cancellationToken)
    {
        if (!IsCurrentAccount(request.FromAccountNumber))
            return Forbid();

        if (Request.Headers.TryGetValue("Idempotency-Key", out var keyValues))
        {
            var key = keyValues.ToString();
            var requestHash = ApiJson.Hash(request);

            if (_idempotencyStore.TryGet(key, out var cached))
            {
                if (!string.Equals(cached.RequestHash, requestHash, StringComparison.Ordinal))
                    return Conflict(new ProblemDetails
                    {
                        Status = StatusCodes.Status409Conflict,
                        Title = "Chave de idempotência reutilizada.",
                        Detail = "A mesma Idempotency-Key foi usada com um payload diferente."
                    });

                var contentType = cached.StatusCode >= 400
                    ? "application/problem+json"
                    : "application/json";

                Response.ContentType = contentType;
                return new ContentResult
                {
                    StatusCode = cached.StatusCode,
                    ContentType = contentType,
                    Content = cached.Body
                };
            }

            var validationResult = await _validator.ValidateAsync(request, cancellationToken);
            if (!validationResult.IsValid)
            {
                var pd = new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Um ou mais erros de validação ocorreram.",
                    Type = "https://httpstatuses.io/400"
                };
                pd.Extensions["errors"] = validationResult.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
                var badVal = ApiJson.Serialize(pd);
                Response.ContentType = "application/problem+json";
                return new ContentResult { StatusCode = 400, ContentType = "application/problem+json", Content = badVal };
            }

            try
            {
                var result = await _transferService.TransferAsync(request, cancellationToken);
                var okBody = ApiJson.Serialize(result);
                _idempotencyStore.Save(key, new IdempotencyEntry { StatusCode = 200, Body = okBody, RequestHash = requestHash });
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                var message = ex.Message.ToLower();
                object bodyObj;
                int status;

                if (message.Contains("não encontrada"))
                {
                    status = 404;
                    bodyObj = new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = "Recurso não encontrado.", Detail = ex.Message };
                }
                else
                {
                    status = 400;
                    bodyObj = new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = "Requisição inválida.", Detail = ex.Message };
                }

                var bodyStr = ApiJson.Serialize(bodyObj);
                _idempotencyStore.Save(key, new IdempotencyEntry { StatusCode = status, Body = bodyStr, RequestHash = requestHash });
                Response.ContentType = "application/problem+json";
                return new ContentResult { StatusCode = status, ContentType = "application/problem+json", Content = bodyStr };
            }
        }

        var validationResult2 = await _validator.ValidateAsync(request, cancellationToken);
        if (!validationResult2.IsValid)
        {
            var problemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Um ou mais erros de validação ocorreram.",
                Type = "https://httpstatuses.io/400"
            };
            problemDetails.Extensions["errors"] = validationResult2.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
            return BadRequest(problemDetails);
        }

        try
        {
            var result = await _transferService.TransferAsync(request, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            var message = ex.Message.ToLower();

            if (message.Contains("não encontrada"))
                return NotFound(new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = "Recurso não encontrado.", Detail = ex.Message });

            return BadRequest(new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = "Requisição inválida.", Detail = ex.Message });
        }
    }

    private bool IsCurrentAccount(string accountNumber)
        => string.Equals(User.FindFirst("AccountNumber")?.Value, accountNumber, StringComparison.Ordinal);
}
