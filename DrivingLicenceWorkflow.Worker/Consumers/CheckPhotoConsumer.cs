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

    public CheckPhotoConsumer(
        DrivingLicenceDbContext dbContext,
        IPhotoCheckService photoCheckService)
    {
        _dbContext = dbContext;
        _photoCheckService = photoCheckService;
    }

    public async Task Consume(ConsumeContext<CheckPhoto> context)
    {
        var application = await _dbContext.Applications
            .SingleOrDefaultAsync(a => a.Id == context.Message.ApplicationId, context.CancellationToken)
            ?? throw new WorkflowValidationException(
                $"Application {context.Message.ApplicationId} was not found.");

        var result = _photoCheckService.Check(application.PhotoBase64);

        if (!result.IsAcceptable)
        {
            if (string.IsNullOrWhiteSpace(result.FailureReason))
            {
                throw new WorkflowValidationException(
                    $"Photo check failed for application {application.Id} without providing a failure reason.");
            }

            await context.Publish(
                new PhotoCheckFailed(application.Id, result.FailureReason),
                context.CancellationToken);
            return;
        }

        application.PhotoQualityScore = result.QualityScore;
        await _dbContext.SaveChangesAsync(context.CancellationToken);

        await context.Publish(
            new PhotoChecked(application.Id, result.QualityScore),
            context.CancellationToken);
    }
}
