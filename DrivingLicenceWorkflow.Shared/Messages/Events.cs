namespace DrivingLicenceWorkflow.Shared.Messages;

// Correlated to the saga by ApplicationId (saga CorrelationId == ApplicationId).
public record ApplicationSubmitted(Guid ApplicationId);

public record ApplicationValidated(Guid ApplicationId);

public record ApplicationValidationFailed(Guid ApplicationId, string Reason);

public record PhotoChecked(Guid ApplicationId, int QualityScore);

public record PhotoCheckFailed(Guid ApplicationId, string Reason);

public record RiskApproved(Guid ApplicationId);

public record RiskManualReviewRequired(Guid ApplicationId, string Reason);

public record ManualReviewApproved(Guid ApplicationId);

public record ManualReviewRejected(Guid ApplicationId, string RejectionReason);
