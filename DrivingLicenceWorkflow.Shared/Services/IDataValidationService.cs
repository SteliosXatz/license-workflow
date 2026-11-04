namespace DrivingLicenceWorkflow.Shared.Services;

public interface IDataValidationService
{
    ValidationResult Validate(ApplicationData data);
}

public record ApplicationData(
    string? FirstName, string? LastName, DateTime DateOfBirth,
    string? NationalId, string? Email, string? PhoneNumber,
    string? Address, string? LicenceCategory, string? PhotoBase64);

public record ValidationResult(bool IsValid, IReadOnlyList<string> Errors);
