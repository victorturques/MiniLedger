using FluentValidation.TestHelper;
using MiniLedger.Application.DTOs;
using MiniLedger.Application.Validators;

namespace MiniLedger.Tests;

public class CreateAccountRequestValidatorTests
{
    private readonly CreateAccountRequestValidator _validator = new();

    [Fact]
    public void Should_Have_Error_When_HolderName_Is_Empty()
    {
        var request = new CreateAccountRequest("", "1234", "1234", 0m);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.HolderName);
    }

    [Fact]
    public void Should_Have_Error_When_HolderName_Exceeds_MaxLength()
    {
        var longName = new string('A', 151);
        var request = new CreateAccountRequest(longName, "1234", "1234", 0m);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.HolderName);
    }

    [Fact]
    public void Should_Have_Error_When_Password_Is_Empty()
    {
        var request = new CreateAccountRequest("Joao Silva", "", "", 0m);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Password);
    }

    [Fact]
    public void Should_Have_Error_When_Password_Is_Less_Than_Four_Characters()
    {
        var request = new CreateAccountRequest("Joao Silva", "123", "123", 0m);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Password);
    }

    [Fact]
    public void Should_Have_Error_When_ConfirmPassword_Is_Empty()
    {
        var request = new CreateAccountRequest("Joao Silva", "1234", "", 0m);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.ConfirmPassword);
    }

    [Fact]
    public void Should_Have_Error_When_Passwords_Do_Not_Match()
    {
        var request = new CreateAccountRequest("Joao Silva", "1234", "4321", 0m);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.ConfirmPassword);
    }

    [Fact]
    public void Should_Have_Error_When_InitialBalance_Is_Negative()
    {
        var request = new CreateAccountRequest("Joao Silva", "1234", "1234", -1m);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.InitialBalance);
    }

    [Fact]
    public void Should_Have_Error_When_InitialBalance_Has_More_Than_Two_Decimals()
    {
        var request = new CreateAccountRequest("Joao Silva", "1234", "1234", 10.999m);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.InitialBalance);
    }

    [Fact]
    public void Should_Have_Error_When_InitialBalance_Exceeds_Maximum_Precision()
    {
        var request = new CreateAccountRequest("Joao Silva", "1234", "1234", 123456789012345678.99m);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.InitialBalance);
    }

    [Fact]
    public void Should_Not_Have_Errors_When_Valid_With_Zero_Balance()
    {
        var request = new CreateAccountRequest("Maria Souza", "abcd", "abcd", 0m);
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Should_Not_Have_Errors_When_Valid_With_Positive_Balance()
    {
        var request = new CreateAccountRequest("Carlos Pereira", "senha123", "senha123", 250.75m);
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }
}
