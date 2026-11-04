using DrivingLicenceWorkflow.Shared.Exceptions;
using DrivingLicenceWorkflow.Shared.Data;
using DrivingLicenceWorkflow.Shared.Messages;
using DrivingLicenceWorkflow.Shared.Services;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace DrivingLicenceWorkflow.Worker.Consumers;

public sealed class CheckPhotoConsumer : IConsumer<CheckPhoto>
{
    private readonly DrivingLicenceDbContext _dbContext;
    private readonly IPhotoCheckService _photoCheckService;
    private readonly ILogger<CheckPhotoConsumer> _logger;

    public CheckPhotoConsumer(
        DrivingLicenceDbContext dbContext,
        IPhotoCheckService photoCheckService,
        ILogger<CheckPhotoConsumer> logger)
    {
        _dbContext = dbContext;
        _photoCheckService = photoCheckService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<CheckPhoto> context)
    {
        var application = await _dbContext.Applications
            .SingleOrDefaultAsync(a => a.Id == context.Message.ApplicationId, context.CancellationToken)
            ?? throw LogMissingApplication(context.Message.ApplicationId);

        var result = _photoCheckService.Check(application.PhotoBase64);

        if (!result.IsAcceptable)
        {
            if (string.IsNullOrWhiteSpace(result.FailureReason))
            {
                _logger.LogError(
                    "Photo check returned no failure details for application {ApplicationId}",
                    application.Id);
                throw new WorkflowValidationException(
                    $"Photo check failed for application {application.Id} without providing a failure reason.");
            }

            _logger.LogWarning(
                "Application {ApplicationId} failed event {Event}",
                application.Id,
                nameof(CheckPhoto));
            await context.Publish(
                new PhotoCheckFailed(application.Id, result.FailureReason),
                context.CancellationToken);
            return;
        }

        application.PhotoQualityScore = result.QualityScore;
        await _dbContext.SaveChangesAsync(context.CancellationToken);

        _logger.LogInformation(
            "Application {ApplicationId} passed event {Event} with photo quality score {PhotoQualityScore}",
            application.Id,
            nameof(CheckPhoto),
            result.QualityScore);
        await context.Publish(
            new PhotoChecked(application.Id, result.QualityScore),
            context.CancellationToken);
    }

    private WorkflowValidationException LogMissingApplication(Guid applicationId)
    {
        _logger.LogError(
            "Application {ApplicationId} was not found while handling event {Event}",
            applicationId,
            nameof(CheckPhoto));
        return new WorkflowValidationException($"Application {applicationId} was not found.");
    }
}
