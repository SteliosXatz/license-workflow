namespace DrivingLicenceWorkflow.Shared.Messages;

public record ValidateApplication(Guid ApplicationId);

public record CheckPhoto(Guid ApplicationId);

public record AssessRisk(Guid ApplicationId);
