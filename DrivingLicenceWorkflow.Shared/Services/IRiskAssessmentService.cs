namespace DrivingLicenceWorkflow.Shared.Services;

public interface IRiskAssessmentService
{
    RiskAssessmentResult Assess(RiskAssessmentInput input);
}

public record RiskAssessmentInput(
    DateTime DateOfBirth, string LicenceCategory,
    string NationalId, int PhotoQualityScore);

public record RiskAssessmentResult(bool RequiresManualReview, string? Reason);
