using DrivingLicenceWorkflow.Api.Contracts;
using DrivingLicenceWorkflow.Shared.Services;
using FluentValidation;

namespace DrivingLicenceWorkflow.Api.Validators;

public sealed class SubmitApplicationRequestValidator : AbstractValidator<SubmitApplicationRequest>
{
    public SubmitApplicationRequestValidator(IDataValidationService dataValidationService)
    {
        RuleFor(request => request).Custom((request, context) =>
        {
            var result = dataValidationService.Validate(new ApplicationData(
                request.FirstName,
                request.LastName,
                request.DateOfBirth,
                request.NationalId,
                request.Email,
                request.PhoneNumber,
                request.Address,
                request.LicenceCategory,
                request.PhotoBase64));

            foreach (var error in result.Errors)
            {
                context.AddFailure(GetPropertyName(error), error);
            }
        });
    }

    private static string GetPropertyName(string error)
    {
        var separator = error.IndexOfAny([':', ' ']);
        var field = separator < 0 ? error : error[..separator];

        return field switch
        {
            "firstName" => nameof(SubmitApplicationRequest.FirstName),
            "lastName" => nameof(SubmitApplicationRequest.LastName),
            "dateOfBirth" => nameof(SubmitApplicationRequest.DateOfBirth),
            "nationalId" => nameof(SubmitApplicationRequest.NationalId),
            "email" => nameof(SubmitApplicationRequest.Email),
            "phoneNumber" => nameof(SubmitApplicationRequest.PhoneNumber),
            "address" => nameof(SubmitApplicationRequest.Address),
            "licenceCategory" => nameof(SubmitApplicationRequest.LicenceCategory),
            "photoBase64" => nameof(SubmitApplicationRequest.PhotoBase64),
            _ => string.Empty
        };
    }
}
