using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MiniLedger.Api.Controllers;
using MiniLedger.Application.DTOs;
using MiniLedger.Application.Services;
using MiniLedger.Application.Validators;
using MiniLedger.Tests.Infrastructure;

namespace MiniLedger.Tests.Controllers;

public class TransfersControllerTests
{
    private static TransfersController CreateController(FakeTransferService transferService, string? idempotencyKey = null, string accountNumber = "00000001")
    {
        var controller = new TransfersController(
            transferService,
            new TransferRequestValidator(),
            new IdempotencyStore());

        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim("AccountNumber", accountNumber) }, "Test"));

        if (!string.IsNullOrEmpty(idempotencyKey))
            httpContext.Request.Headers["Idempotency-Key"] = idempotencyKey;

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        return controller;
    }

    [Fact]
    public async Task Transfer_Should_Return_Ok_When_Request_Is_Valid()
    {
        var transfer = new TransferResponse(Guid.NewGuid(), "00000001", "00000002", 10m, "Success", DateTime.UtcNow);
        var transferService = new FakeTransferService
        {
            Transfer = _ => Task.FromResult(transfer)
        };
        var controller = CreateController(transferService);

        var result = await controller.Transfer(new TransferRequest("00000001", "00000002", 10m), default);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(transfer, ok.Value);
    }

    [Fact]
    public async Task Transfer_Should_Return_BadRequest_Without_Calling_Service_When_Request_Is_Invalid()
    {
        var transferService = new FakeTransferService
        {
            Transfer = _ => throw new InvalidOperationException("não deveria ser chamado")
        };
        var controller = CreateController(transferService);

        var result = await controller.Transfer(new TransferRequest("00000001", "00000001", 10m), default);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.True(problem.Extensions.ContainsKey("errors"));
    }

    [Fact]
    public async Task Transfer_Should_Map_NotFound_Message_To_404()
    {
        var transferService = new FakeTransferService
        {
            Transfer = _ => throw new InvalidOperationException("Conta de origem não encontrada.")
        };
        var controller = CreateController(transferService, accountNumber: "00000099");

        var result = await controller.Transfer(new TransferRequest("00000099", "00000002", 10m), default);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Transfer_Should_Map_Other_Business_Errors_To_400()
    {
        var transferService = new FakeTransferService
        {
            Transfer = _ => throw new InvalidOperationException("Saldo insuficiente.")
        };
        var controller = CreateController(transferService);

        var result = await controller.Transfer(new TransferRequest("00000001", "00000002", 10m), default);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Transfer_With_IdempotencyKey_Should_Replay_Cached_Success_Without_Calling_Service_Again()
    {
        var key = Guid.NewGuid().ToString();
        var calls = 0;
        var transfer = new TransferResponse(Guid.NewGuid(), "00000001", "00000002", 10m, "Success", DateTime.UtcNow);
        var transferService = new FakeTransferService
        {
            Transfer = _ =>
            {
                calls++;
                return Task.FromResult(transfer);
            }
        };
        var controller = CreateController(transferService, key);
        var request = new TransferRequest("00000001", "00000002", 10m);

        var first = await controller.Transfer(request, default);
        var second = await controller.Transfer(request, default);

        Assert.IsType<OkObjectResult>(first);
        var replayed = Assert.IsType<ContentResult>(second);
        Assert.Equal(StatusCodes.Status200OK, replayed.StatusCode);
        Assert.Equal("application/json", replayed.ContentType);
        Assert.Contains("\"transferId\"", replayed.Content);
        Assert.DoesNotContain("\"TransferId\"", replayed.Content);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Transfer_With_IdempotencyKey_Should_Replay_Cached_Business_Error_As_ProblemJson()
    {
        var key = Guid.NewGuid().ToString();
        var calls = 0;
        var transferService = new FakeTransferService
        {
            Transfer = _ =>
            {
                calls++;
                throw new InvalidOperationException("Saldo insuficiente.");
            }
        };
        var controller = CreateController(transferService, key);
        var request = new TransferRequest("00000001", "00000002", 10m);

        var first = await controller.Transfer(request, default);
        var second = await controller.Transfer(request, default);

        var firstResult = Assert.IsType<ContentResult>(first);
        Assert.Equal(StatusCodes.Status400BadRequest, firstResult.StatusCode);
        Assert.Equal("application/problem+json", firstResult.ContentType);

        var replayed = Assert.IsType<ContentResult>(second);
        Assert.Equal(StatusCodes.Status400BadRequest, replayed.StatusCode);
        Assert.Equal("application/problem+json", replayed.ContentType);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Transfer_With_IdempotencyKey_Should_Replay_Cached_NotFound_As_ProblemJson()
    {
        var key = Guid.NewGuid().ToString();
        var transferService = new FakeTransferService
        {
            Transfer = _ => throw new InvalidOperationException("Conta de destino não encontrada.")
        };
        var controller = CreateController(transferService, key);

        var first = await controller.Transfer(new TransferRequest("00000001", "00000099", 10m), default);
        var second = await controller.Transfer(new TransferRequest("00000001", "00000099", 10m), default);

        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsType<ContentResult>(first).StatusCode);

        var replayed = Assert.IsType<ContentResult>(second);
        Assert.Equal(StatusCodes.Status404NotFound, replayed.StatusCode);
        Assert.Equal("application/problem+json", replayed.ContentType);
    }

    [Fact]
    public async Task Transfer_Should_Return_Forbidden_When_Source_Account_Belongs_To_Another_User()
    {
        var transferService = new FakeTransferService
        {
            Transfer = _ => throw new InvalidOperationException("não deveria ser chamado")
        };
        var controller = CreateController(transferService, accountNumber: "00000002");

        var result = await controller.Transfer(new TransferRequest("00000001", "00000002", 10m), default);

        Assert.IsType<ForbidResult>(result);
    }
}
