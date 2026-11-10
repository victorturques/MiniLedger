using FluentValidation.TestHelper;
using MiniLedger.Application.DTOs;
using MiniLedger.Application.Validators;

namespace MiniLedger.Tests;

public class DepositRequestValidatorTests
{
    private readonly DepositRequestValidator _validator = new();

    [Fact]
    public void Should_Have_Error_When_AccountNumber_Is_Empty()
    {
        var request = new DepositRequest("", 50m);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.AccountNumber);
    }

    [Fact]
    public void Should_Have_Error_When_AccountNumber_Is_Not_8_Digits()
    {
        var request = new DepositRequest("1234567", 50m);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.AccountNumber);
    }

    [Fact]
    public void Should_Have_Error_When_Amount_Is_Zero()
    {
        var request = new DepositRequest("00000001", 0m);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Amount);
    }

    [Fact]
    public void Should_Have_Error_When_Amount_Is_Negative()
    {
        var request = new DepositRequest("00000001", -10m);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Amount);
    }

    [Fact]
    public void Should_Have_Error_When_Amount_Has_More_Than_Two_Decimals()
    {
        var request = new DepositRequest("00000001", 10.123m);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Amount);
    }

    [Fact]
    public void Should_Not_Have_Errors_When_Valid_With_Two_Decimals()
    {
        var request = new DepositRequest("00000001", 50.99m);
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Should_Not_Have_Errors_When_Valid_With_Integer_Amount()
    {
        var request = new DepositRequest("00000001", 100m);
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }
}
