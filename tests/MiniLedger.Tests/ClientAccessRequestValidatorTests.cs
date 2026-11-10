using FluentValidation.TestHelper;
using MiniLedger.Application.DTOs;
using MiniLedger.Application.Validators;

namespace MiniLedger.Tests;

public class ClientAccessRequestValidatorTests
{
    private readonly ClientAccessRequestValidator _validator = new();

    [Fact]
    public void Should_Have_Error_When_AccountNumber_Is_Empty()
    {
        var request = new ClientAccessRequest("", "1234");
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.AccountNumber);
    }

    [Fact]
    public void Should_Have_Error_When_AccountNumber_Is_Not_8_Digits()
    {
        var request = new ClientAccessRequest("123", "1234");
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.AccountNumber);
    }

    [Fact]
    public void Should_Have_Error_When_AccountNumber_Contains_Non_Digit()
    {
        var request = new ClientAccessRequest("1234567A", "1234");
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.AccountNumber);
    }

    [Fact]
    public void Should_Have_Error_When_Password_Is_Empty()
    {
        var request = new ClientAccessRequest("00000001", "");
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Password);
    }

    [Fact]
    public void Should_Not_Have_Errors_When_Valid()
    {
        var request = new ClientAccessRequest("00000001", "1234");
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }
}
