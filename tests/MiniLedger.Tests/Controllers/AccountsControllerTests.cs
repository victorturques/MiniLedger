using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MiniLedger.Api.Controllers;
using MiniLedger.Application.DTOs;
using MiniLedger.Application.Services;
using MiniLedger.Application.Validators;
using MiniLedger.Tests.Infrastructure;

namespace MiniLedger.Tests.Controllers;

public class AccountsControllerTests
{
    private static AccountsController CreateController(FakeAccountService accountService, string? accountNumber = null)
    {
        var controller = new AccountsController(
            accountService,
            new CreateAccountRequestValidator(),
            new DepositRequestValidator(),
            new ClientAccessRequestValidator(),
            new IdempotencyStore());

        var httpContext = new DefaultHttpContext();
        if (accountNumber is not null)
        {
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim("AccountNumber", accountNumber) }, "Test"));
        }

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        return controller;
    }

    // ---------------------------------------------------------------------
    // POST /api/accounts
    // ---------------------------------------------------------------------

    [Fact]
    public async Task CreateAccount_Should_Return_CreatedAtAction_When_Request_Is_Valid()
    {
        var response = new AccountResponse(Guid.NewGuid(), "Ana", "00000001", 10m, DateTime.UtcNow);
        CreateAccountRequest? captured = null;

        var accountService = new FakeAccountService
        {
            CreateAccount = request =>
            {
                captured = request;
                return Task.FromResult(response);
            }
        };
        var controller = CreateController(accountService);
        var request = new CreateAccountRequest("Ana", "1234", "1234", 10m);

        var result = await controller.CreateAccount(request, default);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(nameof(AccountsController.GetAccountByNumber), created.ActionName);
        Assert.Same(response, created.Value);
        Assert.Equal(request, captured);
    }

    [Fact]
    public async Task CreateAccount_Should_Return_BadRequest_With_ProblemDetails_When_Request_Is_Invalid()
    {
        var controller = CreateController(new FakeAccountService());

        var result = await controller.CreateAccount(new CreateAccountRequest("", "1234", "1234"), default);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);

        var errors = Assert.IsType<Dictionary<string, string[]>>(problem.Extensions["errors"]);
        Assert.Contains("HolderName", errors.Keys);
    }

    // ---------------------------------------------------------------------
    // GET /api/accounts/by-number/{accountNumber}
    // ---------------------------------------------------------------------

    [Fact]
    public async Task GetAccountByNumber_Should_Return_Ok_When_Account_Exists()
    {
        var response = new AccountResponse(Guid.NewGuid(), "Ana", "00000001", 10m, DateTime.UtcNow);
        var accountService = new FakeAccountService
        {
            GetAccountByNumber = _ => Task.FromResult<AccountResponse?>(response)
        };
        var controller = CreateController(accountService, "00000001");

        var result = await controller.GetAccountByNumber("00000001", default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(response, ok.Value);
    }

    [Fact]
    public async Task GetAccountByNumber_Should_Return_NotFound_When_Account_Does_Not_Exist()
    {
        var accountService = new FakeAccountService
        {
            GetAccountByNumber = _ => Task.FromResult<AccountResponse?>(null)
        };
        var controller = CreateController(accountService, "00000099");

        var result = await controller.GetAccountByNumber("00000099", default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetAccountByNumber_Should_Return_Forbidden_When_Account_Belongs_To_Another_User()
    {
        var accountService = new FakeAccountService
        {
            GetAccountByNumber = _ => throw new InvalidOperationException("não deveria ser chamado")
        };
        var controller = CreateController(accountService, "00000001");

        var result = await controller.GetAccountByNumber("00000002", default);

        Assert.IsType<ForbidResult>(result.Result);
    }

    // ---------------------------------------------------------------------
    // POST /api/accounts/access
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Access_Should_Return_Ok_With_AuthResponse_When_Credentials_Are_Valid()
    {
        var auth = new AuthResponse(Guid.NewGuid(), "Ana", "00000001", 10m, DateTime.UtcNow, "token");
        var accountService = new FakeAccountService
        {
            ValidateClientAccess = _ => Task.FromResult<AuthResponse?>(auth)
        };
        var controller = CreateController(accountService);

        var result = await controller.Access(new ClientAccessRequest("00000001", "1234"), default);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(auth, ok.Value);
    }

    [Fact]
    public async Task Access_Should_Return_Unauthorized_When_Credentials_Are_Invalid()
    {
        var accountService = new FakeAccountService
        {
            ValidateClientAccess = _ => Task.FromResult<AuthResponse?>(null)
        };
        var controller = CreateController(accountService);

        var result = await controller.Access(new ClientAccessRequest("00000001", "errada"), default);

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task Access_Should_Return_BadRequest_Without_Calling_Service_When_Request_Is_Invalid()
    {
        var accountService = new FakeAccountService
        {
            ValidateClientAccess = _ => throw new InvalidOperationException("não deveria ser chamado")
        };
        var controller = CreateController(accountService);

        var result = await controller.Access(new ClientAccessRequest("", ""), default);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.True(problem.Extensions.ContainsKey("errors"));
    }

    // ---------------------------------------------------------------------
    // GET /api/accounts/by-number/{accountNumber}/transactions
    // ---------------------------------------------------------------------

    [Fact]
    public async Task GetTransactions_Should_Clamp_Page_And_PageSize()
    {
        var account = new AccountResponse(Guid.NewGuid(), "Ana", "00000001", 10m, DateTime.UtcNow);
        string? capturedNumber = null;
        var capturedPage = -1;
        var capturedPageSize = -1;

        var accountService = new FakeAccountService
        {
            GetAccountByNumber = _ => Task.FromResult<AccountResponse?>(account),
            GetTransactions = (number, page, pageSize) =>
            {
                capturedNumber = number;
                capturedPage = page;
                capturedPageSize = pageSize;
                return Task.FromResult(new PagedResult<TransactionResponse>(
                    Array.Empty<TransactionResponse>(), page, pageSize, 0, 0));
            }
        };
        var controller = CreateController(accountService, "00000001");

        var result = await controller.GetTransactions("00000001", page: 0, pageSize: 500, cancellationToken: default);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("00000001", capturedNumber);
        Assert.Equal(1, capturedPage);
        Assert.Equal(20, capturedPageSize);
    }

    [Fact]
    public async Task GetTransactions_Should_Return_NotFound_When_Account_Does_Not_Exist()
    {
        var accountService = new FakeAccountService
        {
            GetAccountByNumber = _ => Task.FromResult<AccountResponse?>(null)
        };
        var controller = CreateController(accountService, "00000099");

        var result = await controller.GetTransactions("00000099", 1, 20, default);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    // ---------------------------------------------------------------------
    // POST /api/accounts/deposit
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Deposit_Should_Return_Ok_When_Request_Is_Valid()
    {
        var deposit = new DepositResponse("00000001", "Ana", 10m, 110m, Guid.NewGuid(), DateTime.UtcNow);
        var accountService = new FakeAccountService
        {
            Deposit = _ => Task.FromResult<DepositResponse?>(deposit)
        };
        var controller = CreateController(accountService, "00000001");

        var result = await controller.Deposit(new DepositRequest("00000001", 10m), idempotencyKey: null, default);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(deposit, ok.Value);
    }

    [Fact]
    public async Task Deposit_Should_Return_NotFound_When_Account_Does_Not_Exist()
    {
        var accountService = new FakeAccountService
        {
            Deposit = _ => Task.FromResult<DepositResponse?>(null)
        };
        var controller = CreateController(accountService, "00000099");

        var result = await controller.Deposit(new DepositRequest("00000099", 10m), idempotencyKey: null, default);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Deposit_Should_Return_BadRequest_When_Service_Throws_ArgumentException()
    {
        var accountService = new FakeAccountService
        {
            Deposit = _ => throw new ArgumentException("valor inválido")
        };
        var controller = CreateController(accountService, "00000001");

        var result = await controller.Deposit(new DepositRequest("00000001", 10m), idempotencyKey: null, default);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Deposit_Should_Return_BadRequest_When_Request_Is_Invalid()
    {
        var accountService = new FakeAccountService
        {
            Deposit = _ => throw new InvalidOperationException("não deveria ser chamado")
        };
        var controller = CreateController(accountService, "00000001");

        var result = await controller.Deposit(new DepositRequest("00000001", 0m), idempotencyKey: null, default);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.True(problem.Extensions.ContainsKey("errors"));
    }

    [Fact]
    public async Task Deposit_With_IdempotencyKey_Should_Replay_Cached_Success_Without_Calling_Service_Again()
    {
        var key = Guid.NewGuid().ToString();
        var calls = 0;
        var deposit = new DepositResponse("00000001", "Ana", 10m, 110m, Guid.NewGuid(), DateTime.UtcNow);
        var accountService = new FakeAccountService
        {
            Deposit = _ =>
            {
                calls++;
                return Task.FromResult<DepositResponse?>(deposit);
            }
        };
        var controller = CreateController(accountService, "00000001");
        var request = new DepositRequest("00000001", 10m);

        var first = await controller.Deposit(request, key, default);
        var second = await controller.Deposit(request, key, default);

        Assert.IsType<OkObjectResult>(first);
        var replayed = Assert.IsType<ContentResult>(second);
        Assert.Equal(StatusCodes.Status200OK, replayed.StatusCode);
        Assert.Equal("application/json", replayed.ContentType);
        Assert.Contains("\"accountNumber\":\"00000001\"", replayed.Content);
        Assert.DoesNotContain("\"AccountNumber\"", replayed.Content);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Deposit_With_IdempotencyKey_Should_Replay_Cached_NotFound_As_ProblemJson()
    {
        var key = Guid.NewGuid().ToString();
        var calls = 0;
        var accountService = new FakeAccountService
        {
            Deposit = _ =>
            {
                calls++;
                return Task.FromResult<DepositResponse?>(null);
            }
        };
        var controller = CreateController(accountService, "00000099");
        var request = new DepositRequest("00000099", 10m);

        var first = await controller.Deposit(request, key, default);
        var second = await controller.Deposit(request, key, default);

        var firstResult = Assert.IsType<ContentResult>(first);
        Assert.Equal(StatusCodes.Status404NotFound, firstResult.StatusCode);
        Assert.Equal("application/problem+json", firstResult.ContentType);

        var replayed = Assert.IsType<ContentResult>(second);
        Assert.Equal(StatusCodes.Status404NotFound, replayed.StatusCode);
        Assert.Equal("application/problem+json", replayed.ContentType);
        Assert.Equal(1, calls);
    }
}
