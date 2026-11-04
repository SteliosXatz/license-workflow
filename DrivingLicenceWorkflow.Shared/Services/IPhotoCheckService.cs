namespace DrivingLicenceWorkflow.Shared.Services;

public interface IPhotoCheckService
{
    PhotoCheckResult Check(string? photoBase64);
}

public record PhotoCheckResult(bool IsAcceptable, int QualityScore, string? FailureReason);
