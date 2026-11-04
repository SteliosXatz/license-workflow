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
    private readonly ILogger<ValidateApplicationConsumer> _logger;

    public ValidateApplicationConsumer(
        DrivingLicenceDbContext dbContext,
        IDataValidationService validationService,
        ILogger<ValidateApplicationConsumer> logger)
    {
        _dbContext = dbContext;
        _validationService = validationService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ValidateApplication> context)
    {
        var application = await _dbContext.Applications
            .SingleOrDefaultAsync(a => a.Id == context.Message.ApplicationId, context.CancellationToken)
            ?? throw LogMissingApplication(context.Message.ApplicationId);

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
            _logger.LogInformation(
                "Application {ApplicationId} passed event {Event}",
                application.Id,
                nameof(ValidateApplication));
            await context.Publish(
                new ApplicationValidated(application.Id),
                context.CancellationToken);
            return;
        }

        if (result.Errors.Count == 0)
        {
            _logger.LogError(
                "Validation returned no failure details for application {ApplicationId}",
                application.Id);
            throw new WorkflowValidationException(
                $"Validation failed for application {application.Id} without providing an error reason.");
        }

        _logger.LogWarning(
            "Application {ApplicationId} failed event {Event} with {@ValidationErrors}",
            application.Id,
            nameof(ValidateApplication),
            result.Errors);
        await context.Publish(
            new ApplicationValidationFailed(application.Id, string.Join(" ", result.Errors)),
            context.CancellationToken);
    }

    private WorkflowValidationException LogMissingApplication(Guid applicationId)
    {
        _logger.LogError(
            "Application {ApplicationId} was not found while handling event {Event}",
            applicationId,
            nameof(ValidateApplication));
        return new WorkflowValidationException($"Application {applicationId} was not found.");
    }
}
