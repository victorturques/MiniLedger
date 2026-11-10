using FluentValidation;
using MiniLedger.Application.DTOs;

namespace MiniLedger.Application.Validators;

public class TransferRequestValidator : AbstractValidator<TransferRequest>
{
    public TransferRequestValidator()
    {
        RuleFor(x => x.FromAccountNumber)
            .NotEmpty().WithMessage("FromAccountNumber é obrigatório.")
            .Matches(@"^\d{8}$").WithMessage("FromAccountNumber deve conter 8 dígitos.");

        RuleFor(x => x.ToAccountNumber)
            .NotEmpty().WithMessage("ToAccountNumber é obrigatório.")
            .Matches(@"^\d{8}$").WithMessage("ToAccountNumber deve conter 8 dígitos.")
            .NotEqual(x => x.FromAccountNumber)
            .WithMessage("FromAccountNumber e ToAccountNumber não podem ser iguais.")
            .When(x => !string.IsNullOrWhiteSpace(x.FromAccountNumber) && x.FromAccountNumber.Length == 8);

        RuleFor(x => x.Amount)
            .GreaterThan(0).WithMessage("Amount deve ser maior que 0.")
            .PrecisionScale(18, 2, false).WithMessage("O valor deve ter no máximo 2 casas decimais.");
    }
}
