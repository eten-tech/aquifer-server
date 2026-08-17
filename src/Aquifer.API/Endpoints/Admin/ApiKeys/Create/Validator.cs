using Aquifer.Data.Entities;
using FastEndpoints;
using FluentValidation;

namespace Aquifer.API.Endpoints.Admin.ApiKeys.Create;

public class Validator : Validator<Request>
{
    public Validator()
    {
        RuleFor(x => x.Scope).IsInEnum();
        RuleFor(x => x.Scope).Must(scope => scope != ApiKeyScope.None).WithMessage("A valid scope is required.");
        RuleFor(x => x.Organization).MaximumLength(64);
        RuleFor(x => x.ContactName).MaximumLength(64);
        RuleFor(x => x.Email).MaximumLength(64).EmailAddress().When(x => x.Email is not null);
        RuleFor(x => x.Phone).MaximumLength(32);
        RuleFor(x => x.UseCase).MaximumLength(256);

        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.ContactName) || !string.IsNullOrWhiteSpace(x.Organization))
            .WithMessage("Either ContactName or Organization is required.");
    }
}
