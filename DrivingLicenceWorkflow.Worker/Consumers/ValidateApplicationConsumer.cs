using DrivingLicenceWorkflow.Shared.Exceptions;
using DrivingLicenceWorkflow.Shared.Data;
using DrivingLicenceWorkflow.Shared.Messages;
using DrivingLicenceWorkflow.Shared.Services;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace DrivingLicenceWorkflow.Worker.Consumers;

public sealed class ValidateApplicationConsumer : IConsumer<ValidateApplication>
{
    private readonly DrivingLicenceDbContext _dbContext;
    private readonly IDataValidationService _validationService;

    public ValidateApplicationConsumer(
        DrivingLicenceDbContext dbContext,
        IDataValidationService validationService)
    {
        _dbContext = dbContext;
        _validationService = validationService;
    }

    public async Task Consume(ConsumeContext<ValidateApplication> context)
    {
        var application = await _dbContext.Applications
            .SingleOrDefaultAsync(a => a.Id == context.Message.ApplicationId, context.CancellationToken)
            ?? throw new WorkflowValidationException(
                $"Application {context.Message.ApplicationId} was not found.");

        var result = _validationService.Validate(new ApplicationData(
            application.FirstName,
            application.LastName,
            application.DateOfBirth,
            application.NationalId,
            application.Email,
            application.PhoneNumber,
            application.Address,
            application.LicenceCategory,
            application.PhotoBase64));

        if (result.IsValid)
        {
            await context.Publish(
                new ApplicationValidated(application.Id),
                context.CancellationToken);
            return;
        }

        if (result.Errors.Count == 0)
        {
            throw new WorkflowValidationException(
                $"Validation failed for application {application.Id} without providing an error reason.");
        }

        await context.Publish(
            new ApplicationValidationFailed(application.Id, string.Join(" ", result.Errors)),
            context.CancellationToken);
    }
}
