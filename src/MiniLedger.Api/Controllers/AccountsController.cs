using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MiniLedger.Api.Serialization;
using MiniLedger.Application.DTOs;
using MiniLedger.Application.Interfaces;
using MiniLedger.Application.Services;
using FluentValidation;

namespace MiniLedger.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AccountsController : ControllerBase
{
    private readonly IAccountService _accountService;
    private readonly IValidator<CreateAccountRequest> _createValidator;
    private readonly IValidator<DepositRequest> _depositValidator;
    private readonly IValidator<ClientAccessRequest> _accessValidator;
    private readonly IdempotencyStore _idempotencyStore;

    public AccountsController(IAccountService accountService, IValidator<CreateAccountRequest> createValidator, IValidator<DepositRequest> depositValidator, IValidator<ClientAccessRequest> accessValidator, IdempotencyStore idempotencyStore)
    {
        _accountService = accountService;
        _createValidator = createValidator;
        _depositValidator = depositValidator;
        _accessValidator = accessValidator;
        _idempotencyStore = idempotencyStore;
    }

    [HttpPost]
    [AllowAnonymous]
    public async Task<ActionResult<AccountResponse>> CreateAccount([FromBody] CreateAccountRequest request, CancellationToken cancellationToken)
    {
        var validationResult = await _createValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var problemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Um ou mais erros de validação ocorreram.",
                Type = "https://httpstatuses.io/400"
            };
            problemDetails.Extensions["errors"] = validationResult.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
            return BadRequest(problemDetails);
        }

        var result = await _accountService.CreateAccountAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetAccountByNumber), new { accountNumber = result.AccountNumber }, result);
    }

    [HttpGet("by-number/{accountNumber}")]
    [Authorize]
    public async Task<ActionResult<AccountResponse>> GetAccountByNumber(string accountNumber, CancellationToken cancellationToken)
    {
        if (!IsCurrentAccount(accountNumber))
            return Forbid();

        var result = await _accountService.GetAccountByNumberAsync(accountNumber, cancellationToken);
        if (result is null)
            return NotFound();

        return Ok(result);
    }

    [HttpGet("by-number/{accountNumber}/transactions")]
    [Authorize]
    public async Task<IActionResult> GetTransactions(string accountNumber, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        if (!IsCurrentAccount(accountNumber))
            return Forbid();

        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        var account = await _accountService.GetAccountByNumberAsync(accountNumber, cancellationToken);
        if (account is null)
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Recurso não encontrado.",
                Detail = "Conta não encontrada."
            });

        var transactions = await _accountService.GetTransactionsByAccountNumberAsync(accountNumber, page, pageSize, cancellationToken);
        return Ok(transactions);
    }

    [HttpPost("access")]
    [AllowAnonymous]
    public async Task<IActionResult> Access([FromBody] ClientAccessRequest request, CancellationToken cancellationToken)
    {
        var validationResult = await _accessValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var problemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Um ou mais erros de validação ocorreram.",
                Type = "https://httpstatuses.io/400"
            };
            problemDetails.Extensions["errors"] = validationResult.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
            return BadRequest(problemDetails);
        }

        var result = await _accountService.ValidateClientAccessAsync(request, cancellationToken);
        if (result is null)
            return Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Não autorizado.",
                Detail = "Número da conta ou senha inválidos."
            });

        return Ok(result);
    }

    [HttpPost("deposit")]
    [Authorize]
    public async Task<IActionResult> Deposit([FromBody] DepositRequest request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken cancellationToken)
    {
        if (!IsCurrentAccount(request.AccountNumber))
            return Forbid();

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var requestHash = ApiJson.Hash(request);

            if (_idempotencyStore.TryGet(idempotencyKey, out var cached))
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

            var validationResult = await _depositValidator.ValidateAsync(request, cancellationToken);
            if (!validationResult.IsValid)
            {
                var problemDetails = new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Um ou mais erros de validação ocorreram.",
                    Type = "https://httpstatuses.io/400"
                };
                problemDetails.Extensions["errors"] = validationResult.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
                var badVal = ApiJson.Serialize(problemDetails);
                Response.ContentType = "application/problem+json";
                return new ContentResult { StatusCode = 400, ContentType = "application/problem+json", Content = badVal };
            }

            try
            {
                var result = await _accountService.DepositAsync(request, cancellationToken);
                if (result is null)
                {
                    var notFound = new ProblemDetails
                    {
                        Status = StatusCodes.Status404NotFound,
                        Title = "Recurso não encontrado.",
                        Detail = "Conta não encontrada."
                    };
                    var notFoundBody = ApiJson.Serialize(notFound);
                    _idempotencyStore.Save(idempotencyKey, new IdempotencyEntry { StatusCode = 404, Body = notFoundBody, RequestHash = requestHash });
                    Response.ContentType = "application/problem+json";
                    return new ContentResult { StatusCode = 404, ContentType = "application/problem+json", Content = notFoundBody };
                }

                var okBody = ApiJson.Serialize(result);
                _idempotencyStore.Save(idempotencyKey, new IdempotencyEntry { StatusCode = 200, Body = okBody, RequestHash = requestHash });
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                var badReq = new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Requisição inválida.",
                    Detail = ex.Message
                };
                var badReqBody = ApiJson.Serialize(badReq);
                _idempotencyStore.Save(idempotencyKey, new IdempotencyEntry { StatusCode = 400, Body = badReqBody, RequestHash = requestHash });
                Response.ContentType = "application/problem+json";
                return new ContentResult { StatusCode = 400, ContentType = "application/problem+json", Content = badReqBody };
            }
            catch (InvalidOperationException ex)
            {
                var badReq = new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Requisição inválida.",
                    Detail = ex.Message
                };
                var badReqBody = ApiJson.Serialize(badReq);
                _idempotencyStore.Save(idempotencyKey, new IdempotencyEntry { StatusCode = 400, Body = badReqBody, RequestHash = requestHash });
                Response.ContentType = "application/problem+json";
                return new ContentResult { StatusCode = 400, ContentType = "application/problem+json", Content = badReqBody };
            }
        }

        var validationResult2 = await _depositValidator.ValidateAsync(request, cancellationToken);
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
            var result = await _accountService.DepositAsync(request, cancellationToken);
            if (result is null)
                return NotFound(new ProblemDetails
                {
                    Status = StatusCodes.Status404NotFound,
                    Title = "Recurso não encontrado.",
                    Detail = "Conta não encontrada."
                });

            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Requisição inválida.",
                Detail = ex.Message
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Requisição inválida.",
                Detail = ex.Message
            });
        }
    }

    private bool IsCurrentAccount(string accountNumber)
        => string.Equals(User.FindFirst("AccountNumber")?.Value, accountNumber, StringComparison.Ordinal);
}

