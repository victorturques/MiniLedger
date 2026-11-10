using FluentValidation;
using MiniLedger.Application.DTOs;

namespace MiniLedger.Application.Validators;

public class DepositRequestValidator : AbstractValidator<DepositRequest>
{
    public DepositRequestValidator()
    {
        RuleFor(x => x.AccountNumber)
            .NotEmpty().WithMessage("O número da conta é obrigatório.")
            .Matches(@"^\d{8}$").WithMessage("O número da conta deve ter 8 dígitos.");

        RuleFor(x => x.Amount)
            .GreaterThan(0).WithMessage("O valor do depósito deve ser maior que zero.")
            .PrecisionScale(18, 2, false).WithMessage("O valor deve ter no máximo 2 casas decimais.");
    }
}
