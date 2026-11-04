using DrivingLicenceWorkflow.Api.Controllers;
using DrivingLicenceWorkflow.Api.Observability;
using DrivingLicenceWorkflow.Shared.Data;
using DrivingLicenceWorkflow.Shared.Domain;
using DrivingLicenceWorkflow.Tests.BusinessRules;
using DrivingLicenceWorkflow.Worker.Services;
using FsCheck.Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace DrivingLicenceWorkflow.Tests.Saga;

public sealed class AuditTrailProperties
{
    private static readonly (ApplicationStatus From, ApplicationStatus To, string Event)[] SuccessfulTransitions =
    [
        (ApplicationStatus.Submitted, ApplicationStatus.ValidatingData, "ApplicationSubmitted"),
        (ApplicationStatus.ValidatingData, ApplicationStatus.CheckingPhoto, "ApplicationValidated"),
        (ApplicationStatus.CheckingPhoto, ApplicationStatus.RiskAssessment, "PhotoChecked"),
        (ApplicationStatus.RiskAssessment, ApplicationStatus.Approved, "RiskApproved")
    ];

    [Property(MaxTest = 100)]
    // Feature: driving-licence-workflow, Property 7: Audit trail count invariant
    public void N_transitions_produce_exactly_N_audit_entries_and_update_status(int requestedSteps)
    {
        var steps = Math.Clamp(requestedSteps & int.MaxValue, 1, SuccessfulTransitions.Length);
        using var db = CreateDatabase();
        var timeProvider = new FakeTimeProvider(TestData.Now);
        var application = NewApplication();
        db.Applications.Add(application);
        db.SaveChanges();
        var audit = new AuditService(db, timeProvider, NullLogger<AuditService>.Instance);

        for (var index = 0; index < steps; index++)
        {
            var transition = SuccessfulTransitions[index];
            audit.RecordTransitionAsync(
                    application.Id,
                    transition.From,
                    transition.To,
                    transition.Event,
                    index == 0 ? null : $"step-{index}")
                .GetAwaiter().GetResult();
            db.SaveChanges();
            timeProvider.Advance(TimeSpan.FromSeconds(1));
        }

        var entries = db.AuditTrailEntries.AsNoTracking().Where(entry => entry.ApplicationId == application.Id).ToArray();
        Assert.Equal(steps, entries.Length);
        Assert.All(entries, entry =>
        {
            Assert.NotEqual(default, entry.Timestamp);
            Assert.False(string.IsNullOrWhiteSpace(entry.Event));
        });
        Assert.Equal(SuccessfulTransitions[steps - 1].To, db.Applications.Single().Status);
    }

    [Property(MaxTest = 100)]
    // Feature: driving-licence-workflow, Property 8: Audit trail ordering
    public void History_is_returned_chronologically_with_transition_details(int requestedSteps)
    {
        var steps = Math.Clamp(requestedSteps & int.MaxValue, 1, SuccessfulTransitions.Length);
        using var db = CreateDatabase();
        var application = NewApplication();
        application.AuditTrail =
        new[]
        {
            new AuditTrailEntry
            {
                Id = Guid.NewGuid(),
                ApplicationId = application.Id,
                Timestamp = TestData.Now.AddSeconds(3).UtcDateTime,
                FromStatus = ApplicationStatus.CheckingPhoto,
                ToStatus = ApplicationStatus.RiskAssessment,
                Event = "PhotoChecked",
                Details = "score"
            },
            new AuditTrailEntry
            {
                Id = Guid.NewGuid(),
                ApplicationId = application.Id,
                Timestamp = TestData.Now.UtcDateTime,
                FromStatus = null,
                ToStatus = ApplicationStatus.Submitted,
                Event = "ApplicationSubmitted"
            },
            new AuditTrailEntry
            {
                Id = Guid.NewGuid(),
                ApplicationId = application.Id,
                Timestamp = TestData.Now.AddSeconds(1).UtcDateTime,
                FromStatus = ApplicationStatus.Submitted,
                ToStatus = ApplicationStatus.ValidatingData,
                Event = "ValidationStarted"
            },
            new AuditTrailEntry
            {
                Id = Guid.NewGuid(),
                ApplicationId = application.Id,
                Timestamp = TestData.Now.AddSeconds(2).UtcDateTime,
                FromStatus = ApplicationStatus.ValidatingData,
                ToStatus = ApplicationStatus.CheckingPhoto,
                Event = "ApplicationValidated"
            }
        }.OrderBy(entry => entry.Timestamp).Take(steps).ToList();
        db.Applications.Add(application);
        db.SaveChanges();

        var controller = new ApplicationsController(
            db,
            null!,
            null!,
            null!,
            TimeProvider.System,
            NullLogger<ApplicationsController>.Instance,
            new ApplicationWorkflowMetrics());
        var response = controller.GetHistory(application.Id, CancellationToken.None).GetAwaiter().GetResult();
        var result = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(response.Result);
        var history = Assert.IsType<DrivingLicenceWorkflow.Api.Contracts.AuditTrailResponse>(result.Value);
        var entries = history.Entries;

        Assert.NotNull(entries);
        Assert.Equal(steps, entries!.Count);
        Assert.Equal(entries.OrderBy(entry => entry.Timestamp), entries);
        Assert.All(entries, entry =>
        {
            Assert.NotEqual(default, entry.Timestamp);
            Assert.Contains(entry.ToStatus, Enum.GetValues<ApplicationStatus>());
            Assert.False(string.IsNullOrWhiteSpace(entry.Event));
        });
    }

    private static DrivingLicenceDbContext CreateDatabase() =>
        new(new DbContextOptionsBuilder<DrivingLicenceDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static Application NewApplication() => new()
    {
        Id = Guid.NewGuid(),
        Status = ApplicationStatus.Submitted,
        CreatedAt = TestData.Now.UtcDateTime,
        UpdatedAt = TestData.Now.UtcDateTime
    };
}
