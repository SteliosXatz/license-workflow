using DrivingLicenceWorkflow.Api.Contracts;
using FluentValidation;

namespace DrivingLicenceWorkflow.Api.Validators;

public sealed class ManualReviewActionRequestValidator : AbstractValidator<ManualReviewActionRequest>
{
    public ManualReviewActionRequestValidator()
    {
        RuleFor(request => request.Reason)
            .NotEmpty()
            .WithMessage("A rejection reason is required.")
            .MaximumLength(1000)
            .WithMessage("The rejection reason must be 1000 characters or fewer.");
    }
}
