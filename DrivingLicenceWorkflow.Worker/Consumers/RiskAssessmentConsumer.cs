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

    public RiskAssessmentConsumer(
        DrivingLicenceDbContext dbContext,
        IRiskAssessmentService riskAssessmentService)
    {
        _dbContext = dbContext;
        _riskAssessmentService = riskAssessmentService;
    }

    public async Task Consume(ConsumeContext<AssessRisk> context)
    {
        var application = await _dbContext.Applications
            .SingleOrDefaultAsync(a => a.Id == context.Message.ApplicationId, context.CancellationToken)
            ?? throw new WorkflowValidationException(
                $"Application {context.Message.ApplicationId} was not found.");

        var photoQualityScore = application.PhotoQualityScore
            ?? throw new WorkflowValidationException(
                $"Application {application.Id} has no stored photo quality score.");

        var result = _riskAssessmentService.Assess(new RiskAssessmentInput(
            application.DateOfBirth,
            application.LicenceCategory,
            application.NationalId,
            photoQualityScore));

        if (result.RequiresManualReview)
        {
            if (string.IsNullOrWhiteSpace(result.Reason))
            {
                throw new WorkflowValidationException(
                    $"Risk assessment requires manual review for application {application.Id} without providing a reason.");
            }

            await context.Publish(
                new RiskManualReviewRequired(application.Id, result.Reason),
                context.CancellationToken);
            return;
        }

        await context.Publish(
            new RiskApproved(application.Id),
            context.CancellationToken);
    }
}
