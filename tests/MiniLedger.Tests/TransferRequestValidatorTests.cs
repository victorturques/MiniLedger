using FluentValidation.TestHelper;
using MiniLedger.Application.DTOs;
using MiniLedger.Application.Validators;

namespace MiniLedger.Tests;

public class TransferRequestValidatorTests
{
    private readonly TransferRequestValidator _validator = new();

    [Fact]
    public void Should_Have_Error_When_FromAccountNumber_Is_Empty()
    {
        var request = new TransferRequest("", "00000002", 10m);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.FromAccountNumber);
    }

    [Fact]
    public void Should_Have_Error_When_FromAccountNumber_Is_Not_8_Digits()
    {
        var request = new TransferRequest("123", "00000002", 10m);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.FromAccountNumber);
    }

    [Fact]
    public void Should_Have_Error_When_ToAccountNumber_Is_Empty()
    {
        var request = new TransferRequest("00000001", "", 10m);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.ToAccountNumber);
    }

    [Fact]
    public void Should_Have_Error_When_ToAccountNumber_Is_Not_8_Digits()
    {
        var request = new TransferRequest("00000001", "0000000", 10m);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.ToAccountNumber);
    }

    [Fact]
    public void Should_Have_Error_When_From_And_To_Are_Same()
    {
        var request = new TransferRequest("00000001", "00000001", 10m);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.ToAccountNumber);
    }

    [Fact]
    public void Should_Have_Error_When_Amount_Is_Zero()
    {
        var request = new TransferRequest("00000001", "00000002", 0m);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Amount);
    }

    [Fact]
    public void Should_Have_Error_When_Amount_Is_Negative()
    {
        var request = new TransferRequest("00000001", "00000002", -5m);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Amount);
    }

    [Fact]
    public void Should_Have_Error_When_Amount_Has_More_Than_Two_Decimals()
    {
        var request = new TransferRequest("00000001", "00000002", 10.999m);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Amount);
    }

    [Fact]
    public void Should_Have_Error_When_Amount_Exceeds_Maximum_Precision()
    {
        var request = new TransferRequest("00000001", "00000002", 123456789012345678.99m);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Amount);
    }

    [Fact]
    public void Should_Not_Have_Errors_When_Valid()
    {
        var request = new TransferRequest("00000001", "00000002", 25.50m);
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }
}
