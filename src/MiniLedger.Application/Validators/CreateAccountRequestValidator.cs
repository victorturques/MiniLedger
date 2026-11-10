using FluentValidation;
using MiniLedger.Application.DTOs;

namespace MiniLedger.Application.Validators;

public class CreateAccountRequestValidator : AbstractValidator<CreateAccountRequest>
{
    public CreateAccountRequestValidator()
    {
        RuleFor(x => x.HolderName)
            .NotEmpty().WithMessage("O nome do titular é obrigatório.")
            .MaximumLength(150).WithMessage("O nome do titular deve ter no máximo 150 caracteres.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("A senha é obrigatória.")
            .MinimumLength(4).WithMessage("A senha deve ter pelo menos 4 caracteres.");

        RuleFor(x => x.ConfirmPassword)
            .NotEmpty().WithMessage("A confirmação da senha é obrigatória.")
            .Equal(x => x.Password).WithMessage("As senhas não coincidem.");

        RuleFor(x => x.InitialBalance)
            .GreaterThanOrEqualTo(0).WithMessage("O saldo inicial não pode ser negativo.")
            .PrecisionScale(18, 2, false).WithMessage("O saldo inicial deve ter no máximo 2 casas decimais.");
    }
}
