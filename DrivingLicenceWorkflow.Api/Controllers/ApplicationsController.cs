using DrivingLicenceWorkflow.Api.Contracts;
using DrivingLicenceWorkflow.Api.Observability;
using DrivingLicenceWorkflow.Api.Validators;
using DrivingLicenceWorkflow.Shared.Data;
using DrivingLicenceWorkflow.Shared.Domain;
using DrivingLicenceWorkflow.Shared.Messages;
using FluentValidation;
using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DrivingLicenceWorkflow.Api.Controllers;

[ApiController]
[Route("api/applications")]
public sealed class ApplicationsController : ControllerBase
{
    private readonly DrivingLicenceDbContext _dbContext;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly IValidator<SubmitApplicationRequest> _submitValidator;
    private readonly IValidator<ManualReviewActionRequest> _manualReviewValidator;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ApplicationsController> _logger;
    private readonly ApplicationWorkflowMetrics _metrics;

    public ApplicationsController(
        DrivingLicenceDbContext dbContext,
        IPublishEndpoint publishEndpoint,
        IValidator<SubmitApplicationRequest> submitValidator,
        IValidator<ManualReviewActionRequest> manualReviewValidator,
        TimeProvider timeProvider,
        ILogger<ApplicationsController> logger,
        ApplicationWorkflowMetrics metrics)
    {
        _dbContext = dbContext;
        _publishEndpoint = publishEndpoint;
        _submitValidator = submitValidator;
        _manualReviewValidator = manualReviewValidator;
        _timeProvider = timeProvider;
        _logger = logger;
        _metrics = metrics;
    }

    /// <summary>Submits an application for asynchronous workflow processing.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(SubmitApplicationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SubmitApplicationResponse>> Submit(
        SubmitApplicationRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await _submitValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            _logger.LogWarning(
                "Application submission validation failed for fields {@ValidationErrors}",
                validation.Errors.Select(error => error.PropertyName).Distinct().ToArray());
            return BadRequest(new ValidationProblemDetails(validation.ToDictionary()));
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var applicationId = Guid.NewGuid();
        var application = new Application
        {
            Id = applicationId,
            FirstName = request.FirstName!,
            LastName = request.LastName!,
            DateOfBirth = request.DateOfBirth,
            NationalId = request.NationalId!,
            Email = request.Email!,
            PhoneNumber = request.PhoneNumber!,
            Address = request.Address!,
            LicenceCategory = request.LicenceCategory!,
            PhotoBase64 = request.PhotoBase64!,
            Status = ApplicationStatus.Submitted,
            CreatedAt = now,
            UpdatedAt = now,
            AuditTrail =
            [
                new AuditTrailEntry
                {
                    Id = Guid.NewGuid(),
                    ApplicationId = applicationId,
                    Timestamp = now,
                    FromStatus = null,
                    ToStatus = ApplicationStatus.Submitted,
                    Event = nameof(ApplicationSubmitted)
                }
            ]
        };

        _dbContext.Applications.Add(application);
        await _publishEndpoint.Publish(new ApplicationSubmitted(application.Id), cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _metrics.RecordApplicationSubmitted();

        _logger.LogInformation(
            "Published workflow event {Event} for application {ApplicationId}",
            nameof(ApplicationSubmitted),
            application.Id);
        return Ok(new SubmitApplicationResponse(application.Id, application.Status));
    }

    /// <summary>Gets the current workflow status of an application.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApplicationStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApplicationStatusResponse>> GetStatus(
        Guid id,
        CancellationToken cancellationToken)
    {
        var application = await _dbContext.Applications
            .AsNoTracking()
            .Where(application => application.Id == id)
            .Select(application => new ApplicationStatusResponse(
                application.Id,
                application.Status,
                application.CreatedAt,
                application.UpdatedAt))
            .SingleOrDefaultAsync(cancellationToken);

        return application is null ? NotFound() : Ok(application);
    }

    /// <summary>Gets the audit history of an application.</summary>
    [HttpGet("{id:guid}/history")]
    [ProducesResponseType(typeof(AuditTrailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AuditTrailResponse>> GetHistory(
        Guid id,
        CancellationToken cancellationToken)
    {
        var application = await _dbContext.Applications
            .AsNoTracking()
            .Include(application => application.AuditTrail)
            .SingleOrDefaultAsync(application => application.Id == id, cancellationToken);

        if (application is null)
        {
            return NotFound();
        }

        var entries = application.AuditTrail
            .OrderBy(entry => entry.Timestamp)
            .ThenBy(entry => entry.Id)
            .Select(entry => new AuditTrailEntryResponse(
                entry.Timestamp,
                entry.FromStatus,
                entry.ToStatus,
                entry.Event,
                entry.Details))
            .ToArray();

        return Ok(new AuditTrailResponse(entries));
    }

    /// <summary>Requests approval of an application awaiting manual review.</summary>
    [HttpPost("{id:guid}/approve")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Approve(
        Guid id,
        CancellationToken cancellationToken)
    {
        var status = await _dbContext.Applications
            .Where(application => application.Id == id)
            .Select(application => (ApplicationStatus?)application.Status)
            .SingleOrDefaultAsync(cancellationToken);

        if (status is null)
        {
            return NotFound();
        }

        if (status != ApplicationStatus.PendingManualReview)
        {
            _logger.LogWarning(
                "Rejected event {Event} for application {ApplicationId} in state {ApplicationStatus}",
                nameof(ManualReviewApproved),
                id,
                status);
            return Conflict(new ProblemDetails
            {
                Title = "Invalid application state transition.",
                Detail = $"Application {id} is in state {status}, not {ApplicationStatus.PendingManualReview}."
            });
        }

        await _publishEndpoint.Publish(new ManualReviewApproved(id), cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Published workflow event {Event} for application {ApplicationId}",
            nameof(ManualReviewApproved),
            id);
        return Ok(new { ApplicationId = id });
    }

    /// <summary>Requests rejection of an application awaiting manual review.</summary>
    [HttpPost("{id:guid}/reject")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reject(
        Guid id,
        ManualReviewActionRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await _manualReviewValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            _logger.LogWarning(
                "Manual rejection validation failed for application {ApplicationId} on fields {@ValidationErrors}",
                id,
                validation.Errors.Select(error => error.PropertyName).Distinct().ToArray());
            return BadRequest(new ValidationProblemDetails(validation.ToDictionary()));
        }

        var status = await _dbContext.Applications
            .Where(application => application.Id == id)
            .Select(application => (ApplicationStatus?)application.Status)
            .SingleOrDefaultAsync(cancellationToken);

        if (status is null)
        {
            return NotFound();
        }

        if (status != ApplicationStatus.PendingManualReview)
        {
            _logger.LogWarning(
                "Rejected event {Event} for application {ApplicationId} in state {ApplicationStatus}",
                nameof(ManualReviewRejected),
                id,
                status);
            return Conflict(new ProblemDetails
            {
                Title = "Invalid application state transition.",
                Detail = $"Application {id} is in state {status}, not {ApplicationStatus.PendingManualReview}."
            });
        }

        await _publishEndpoint.Publish(new ManualReviewRejected(id, request.Reason!), cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Published workflow event {Event} for application {ApplicationId}",
            nameof(ManualReviewRejected),
            id);
        return Ok(new { ApplicationId = id });
    }
}
