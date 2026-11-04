using System.Net.Mail;
using DrivingLicenceWorkflow.Shared.Configuration;
using Microsoft.Extensions.Options;

namespace DrivingLicenceWorkflow.Shared.Services;

public class DataValidationService : IDataValidationService
{
    private readonly DataValidationOptions _options;
    private readonly TimeProvider _timeProvider;

    public DataValidationService(IOptions<DataValidationOptions> options, TimeProvider timeProvider)
    {
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public ValidationResult Validate(ApplicationData data)
    {
        var errors = new List<string>();

        Require(errors, "firstName", data.FirstName);
        Require(errors, "lastName", data.LastName);
        Require(errors, "nationalId", data.NationalId);
        Require(errors, "phoneNumber", data.PhoneNumber);
        Require(errors, "address", data.Address);
        Require(errors, "licenceCategory", data.LicenceCategory);
        Require(errors, "photoBase64", data.PhotoBase64);

        if (string.IsNullOrWhiteSpace(data.Email))
        {
            errors.Add("email is required.");
        }
        else if (!IsValidEmail(data.Email))
        {
            errors.Add("email is not a valid email address.");
        }

        if (data.DateOfBirth == default)
        {
            errors.Add("dateOfBirth is required.");
        }
        else if (AgeCalculator.AgeOn(data.DateOfBirth, _timeProvider.GetUtcNow().UtcDateTime) < _options.MinApplicantAge)
        {
            errors.Add($"dateOfBirth: applicant must be at least {_options.MinApplicantAge} years old.");
        }

        return new ValidationResult(errors.Count == 0, errors);
    }

    private static void Require(List<string> errors, string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{field} is required.");
        }
    }

    // MailAddress also accepts display names ("Jane <j@x.com>"); requiring the parsed address to equal the input rejects them.
    private static bool IsValidEmail(string email)
    {
        var trimmed = email.Trim();
        return MailAddress.TryCreate(trimmed, out var parsed)
            && parsed.Address == trimmed
            && parsed.Host.Contains('.');
    }
}
