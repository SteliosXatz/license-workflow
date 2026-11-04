using DrivingLicenceWorkflow.Shared.Domain;

namespace DrivingLicenceWorkflow.Shared.Services;

public interface IAuditService
{
    Task RecordTransitionAsync(
        Guid applicationId,
        ApplicationStatus? fromStatus,
        ApplicationStatus toStatus,
        string eventName,
        string? details,
        CancellationToken cancellationToken = default);
}
