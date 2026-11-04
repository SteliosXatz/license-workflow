using DrivingLicenceWorkflow.Shared.Exceptions;
using DrivingLicenceWorkflow.Shared.Data;
using DrivingLicenceWorkflow.Shared.Messages;
using DrivingLicenceWorkflow.Shared.Services;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace DrivingLicenceWorkflow.Worker.Consumers;

public sealed class RiskAssessmentConsumer : IConsumer<AssessRisk>
{
    private readonly DrivingLicenceDbContext _dbContext;
    private readonly IRiskAssessmentService _riskAssessmentService;
    private readonly ILogger<RiskAssessmentConsumer> _logger;

    public RiskAssessmentConsumer(
        DrivingLicenceDbContext dbContext,
        IRiskAssessmentService riskAssessmentService,
        ILogger<RiskAssessmentConsumer> logger)
    {
        _dbContext = dbContext;
        _riskAssessmentService = riskAssessmentService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<AssessRisk> context)
    {
        var application = await _dbContext.Applications
            .SingleOrDefaultAsync(a => a.Id == context.Message.ApplicationId, context.CancellationToken)
            ?? throw LogMissingApplication(context.Message.ApplicationId);

        var photoQualityScore = application.PhotoQualityScore
            ?? throw LogMissingPhotoQualityScore(application.Id);

        var result = _riskAssessmentService.Assess(new RiskAssessmentInput(
            application.DateOfBirth,
            application.LicenceCategory,
            application.NationalId,
            photoQualityScore));

        if (result.RequiresManualReview)
        {
            if (string.IsNullOrWhiteSpace(result.Reason))
            {
                _logger.LogError(
                    "Risk assessment returned no manual-review reason for application {ApplicationId}",
                    application.Id);
                throw new WorkflowValidationException(
                    $"Risk assessment requires manual review for application {application.Id} without providing a reason.");
            }

            _logger.LogInformation(
                "Application {ApplicationId} requires manual review after event {Event}",
                application.Id,
                nameof(AssessRisk));
            await context.Publish(
                new RiskManualReviewRequired(application.Id, result.Reason),
                context.CancellationToken);
            return;
        }

        _logger.LogInformation(
            "Application {ApplicationId} passed event {Event}",
            application.Id,
            nameof(AssessRisk));
        await context.Publish(
            new RiskApproved(application.Id),
            context.CancellationToken);
    }

    private WorkflowValidationException LogMissingApplication(Guid applicationId)
    {
        _logger.LogError(
            "Application {ApplicationId} was not found while handling event {Event}",
            applicationId,
            nameof(AssessRisk));
        return new WorkflowValidationException($"Application {applicationId} was not found.");
    }

    private WorkflowValidationException LogMissingPhotoQualityScore(Guid applicationId)
    {
        _logger.LogError(
            "Application {ApplicationId} has no stored photo quality score while handling event {Event}",
            applicationId,
            nameof(AssessRisk));
        return new WorkflowValidationException(
            $"Application {applicationId} has no stored photo quality score.");
    }
}
