using FastEndpoints;
using FluentValidation;

namespace Aquifer.API.Endpoints.Admin.Projects.PreTranslate;

public class Validator : Validator<Request>
{
    public Validator()
    {
        RuleFor(x => x.Id).GreaterThan(0);

        RuleForEach(x => x.ResourceContentIds)
            .GreaterThan(0)
            .When(x => x.ResourceContentIds is not null)
            .WithMessage("Resource Content IDs must be greater than 0.");
    }
}
