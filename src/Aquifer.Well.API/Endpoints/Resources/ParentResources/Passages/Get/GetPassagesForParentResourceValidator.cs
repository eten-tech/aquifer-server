using FastEndpoints;
using FluentValidation;

namespace Aquifer.Well.API.Endpoints.Resources.ParentResources.Passages.Get;

public sealed class Validator : Validator<GetPassagesForParentResourceRequest>
{
    public Validator()
    {
        RuleFor(x => x.LanguageId).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ParentResourceId).GreaterThanOrEqualTo(0);
    }
}