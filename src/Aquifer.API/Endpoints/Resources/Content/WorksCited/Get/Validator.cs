using FastEndpoints;
using FluentValidation;

namespace Aquifer.API.Endpoints.Resources.Content.WorksCited.Get;

public class Validator : Validator<Request>
{
    public Validator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}