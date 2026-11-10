using FluentValidation;
using MiniLedger.Application.DTOs;

namespace MiniLedger.Application.Validators;

public class ClientAccessRequestValidator : AbstractValidator<ClientAccessRequest>
{
    public ClientAccessRequestValidator()
    {
        RuleFor(x => x.AccountNumber)
            .NotEmpty().WithMessage("O número da conta é obrigatório.")
            .Matches(@"^\d{8}$").WithMessage("O número da conta deve ter 8 dígitos.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("A senha é obrigatória.");
    }
}
