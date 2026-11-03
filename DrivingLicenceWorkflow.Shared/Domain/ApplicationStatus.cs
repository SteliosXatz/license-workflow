namespace DrivingLicenceWorkflow.Shared.Domain;

public enum ApplicationStatus
{
    Submitted,
    ValidatingData,
    CheckingPhoto,
    RiskAssessment,
    PendingManualReview,
    Approved,
    Rejected,
    Failed
}
