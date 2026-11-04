using DrivingLicenceWorkflow.Shared.Exceptions;
using DrivingLicenceWorkflow.Shared.Data;
using DrivingLicenceWorkflow.Shared.Domain;
using DrivingLicenceWorkflow.Shared.Services;
using Microsoft.EntityFrameworkCore;

namespace DrivingLicenceWorkflow.Worker.Services;

public sealed class AuditService : IAuditService
{
    private readonly DrivingLicenceDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AuditService> _logger;

    public AuditService(
        DrivingLicenceDbContext dbContext,
        TimeProvider timeProvider,
        ILogger<AuditService> logger)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task RecordTransitionAsync(
        Guid applicationId,
        ApplicationStatus? fromStatus,
        ApplicationStatus toStatus,
        string eventName,
        string? details,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var application = await _dbContext.Applications
            .SingleOrDefaultAsync(application => application.Id == applicationId, cancellationToken)
            ?? throw new WorkflowValidationException($"Application {applicationId} was not found.");

        var timestamp = _timeProvider.GetUtcNow().UtcDateTime;
        _dbContext.AuditTrailEntries.Add(new AuditTrailEntry
        {
            Id = Guid.NewGuid(),
            ApplicationId = applicationId,
            Timestamp = timestamp,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            Event = eventName,
            Details = details
        });

        application.Status = toStatus;
        application.UpdatedAt = timestamp;

        _logger.LogInformation(
            "Recorded workflow transition {@Transition}",
            new
            {
                ApplicationId = applicationId,
                FromStatus = fromStatus,
                ToStatus = toStatus,
                Event = eventName
            });

        // The saga repository saves this shared DbContext with the saga state.
    }
}
