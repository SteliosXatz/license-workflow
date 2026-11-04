using DrivingLicenceWorkflow.Api.Contracts;
using DrivingLicenceWorkflow.Api.Controllers;
using DrivingLicenceWorkflow.Api.Observability;
using DrivingLicenceWorkflow.Api.Validators;
using DrivingLicenceWorkflow.Shared.Configuration;
using DrivingLicenceWorkflow.Shared.Data;
using DrivingLicenceWorkflow.Shared.Domain;
using DrivingLicenceWorkflow.Shared.Messages;
using DrivingLicenceWorkflow.Shared.Services;
using MassTransit.Testing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace DrivingLicenceWorkflow.Tests.Api;

public class ApplicationsControllerTests
{
    [Fact]
    public async Task Submit_valid_application_returns_200_persists_application_and_publishes_event()
    {
        await using var fixture = await ControllerFixture.CreateAsync();

        var result = await fixture.Controller.Submit(ValidRequest(), CancellationToken.None);

        var response = Assert.IsType<SubmitApplicationResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.NotEqual(Guid.Empty, response.ApplicationId);
        Assert.Equal(ApplicationStatus.Submitted, response.Status);

        var application = await fixture.DbContext.Applications
            .Include(item => item.AuditTrail)
            .SingleAsync(item => item.Id == response.ApplicationId);
        Assert.Equal(response.ApplicationId, application.Id);
        Assert.Equal(ApplicationStatus.Submitted, application.Status);
        Assert.Single(application.AuditTrail);
        Assert.Equal(nameof(ApplicationSubmitted), application.AuditTrail[0].Event);
        Assert.True(await fixture.Harness.Published.Any<ApplicationSubmitted>());
    }

    [Fact]
    public async Task Submit_application_with_missing_fields_returns_400_without_persisting_or_publishing()
    {
        await using var fixture = await ControllerFixture.CreateAsync();

        var result = await fixture.Controller.Submit(new SubmitApplicationRequest(), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(await fixture.DbContext.Applications.ToListAsync());
        Assert.False(await fixture.Harness.Published.Any<ApplicationSubmitted>());
    }

    [Fact]
    public async Task GetStatus_for_existing_application_returns_200_with_status_and_timestamps()
    {
        await using var fixture = await ControllerFixture.CreateAsync();
        var application = await fixture.AddApplicationAsync(ApplicationStatus.PendingManualReview);

        var result = await fixture.Controller.GetStatus(application.Id, CancellationToken.None);

        var response = Assert.IsType<ApplicationStatusResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(application.Id, response.Id);
        Assert.Equal(application.Status, response.Status);
        Assert.Equal(application.CreatedAt, response.CreatedAt);
        Assert.Equal(application.UpdatedAt, response.UpdatedAt);
    }

    [Fact]
    public async Task GetStatus_for_unknown_application_returns_404()
    {
        await using var fixture = await ControllerFixture.CreateAsync();

        var result = await fixture.Controller.GetStatus(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Approve_and_reject_for_unknown_application_return_404()
    {
        await using var fixture = await ControllerFixture.CreateAsync();
        var unknownId = Guid.NewGuid();

        var approveResult = await fixture.Controller.Approve(unknownId, CancellationToken.None);
        var rejectResult = await fixture.Controller.Reject(
            unknownId,
            new ManualReviewActionRequest("Valid reason"),
            CancellationToken.None);

        Assert.IsType<NotFoundResult>(approveResult);
        Assert.IsType<NotFoundResult>(rejectResult);
        Assert.False(await fixture.Harness.Published.Any<ManualReviewApproved>());
        Assert.False(await fixture.Harness.Published.Any<ManualReviewRejected>());
    }

    [Fact]
    public async Task Approve_and_reject_conflict_for_every_status_other_than_pending_manual_review_without_publishing_or_transitioning()
    {
        await using var fixture = await ControllerFixture.CreateAsync();
        var applications = new List<Application>();

        foreach (var status in Enum.GetValues<ApplicationStatus>()
                     .Where(status => status != ApplicationStatus.PendingManualReview))
        {
            applications.Add(await fixture.AddApplicationAsync(status));
        }

        foreach (var application in applications)
        {
            var approveResult = await fixture.Controller.Approve(application.Id, CancellationToken.None);
            var rejectResult = await fixture.Controller.Reject(
                application.Id,
                new ManualReviewActionRequest("Valid reason"),
                CancellationToken.None);

            Assert.IsType<ConflictObjectResult>(approveResult);
            Assert.IsType<ConflictObjectResult>(rejectResult);
        }

        Assert.False(await fixture.Harness.Published.Any<ManualReviewApproved>());
        Assert.False(await fixture.Harness.Published.Any<ManualReviewRejected>());

        var storedApplications = await fixture.DbContext.Applications
            .AsNoTracking()
            .Include(application => application.AuditTrail)
            .ToListAsync();
        Assert.Equal(applications.Count, storedApplications.Count);
        foreach (var original in applications)
        {
            var stored = Assert.Single(storedApplications.Where(application => application.Id == original.Id));
            Assert.Equal(original.Status, stored.Status);
            Assert.Single(stored.AuditTrail);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Reject_with_missing_reason_returns_400_without_publishing(string? reason)
    {
        await using var fixture = await ControllerFixture.CreateAsync();
        var application = await fixture.AddApplicationAsync(ApplicationStatus.PendingManualReview);

        var result = await fixture.Controller.Reject(
            application.Id,
            new ManualReviewActionRequest(reason),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.False(await fixture.Harness.Published.Any<ManualReviewRejected>());
    }

    [Fact]
    public async Task Reject_with_reason_longer_than_1000_characters_returns_400_without_publishing()
    {
        await using var fixture = await ControllerFixture.CreateAsync();
        var application = await fixture.AddApplicationAsync(ApplicationStatus.PendingManualReview);

        var result = await fixture.Controller.Reject(
            application.Id,
            new ManualReviewActionRequest(new string('x', 1001)),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.False(await fixture.Harness.Published.Any<ManualReviewRejected>());
    }

    [Fact]
    public async Task Approve_and_reject_for_pending_application_publish_their_events()
    {
        await using var fixture = await ControllerFixture.CreateAsync();
        var approvedApplication = await fixture.AddApplicationAsync(ApplicationStatus.PendingManualReview);
        var rejectedApplication = await fixture.AddApplicationAsync(ApplicationStatus.PendingManualReview);

        var approveResult = await fixture.Controller.Approve(approvedApplication.Id, CancellationToken.None);
        var rejectResult = await fixture.Controller.Reject(
            rejectedApplication.Id,
            new ManualReviewActionRequest("Does not meet requirements"),
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(approveResult);
        Assert.IsType<OkObjectResult>(rejectResult);
        Assert.True(await fixture.Harness.Published.Any<ManualReviewApproved>());
        Assert.True(await fixture.Harness.Published.Any<ManualReviewRejected>());
    }

    private static SubmitApplicationRequest ValidRequest() => new()
    {
        FirstName = "Jane",
        LastName = "Doe",
        DateOfBirth = new DateTime(1990, 1, 1),
        NationalId = "NID-1",
        Email = "jane.doe@example.com",
        PhoneNumber = "+30 210 0000000",
        Address = "1 Main Street",
        LicenceCategory = "B",
        PhotoBase64 = "cGhvdG8="
    };

    private sealed class ControllerFixture : IAsyncDisposable
    {
        private readonly ApplicationWorkflowMetrics _metrics;

        private ControllerFixture(
            InMemoryTestHarness harness,
            DrivingLicenceDbContext dbContext,
            ApplicationWorkflowMetrics metrics)
        {
            Harness = harness;
            DbContext = dbContext;
            _metrics = metrics;
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero));
            var dataValidationService = new DataValidationService(
                Options.Create(new DataValidationOptions()),
                clock);

            Controller = new ApplicationsController(
                DbContext,
                Harness.Bus,
                new SubmitApplicationRequestValidator(dataValidationService),
                new ManualReviewActionRequestValidator(),
                clock,
                NullLogger<ApplicationsController>.Instance,
                _metrics);
        }

        public InMemoryTestHarness Harness { get; }
        public DrivingLicenceDbContext DbContext { get; }
        public ApplicationsController Controller { get; }

        public static async Task<ControllerFixture> CreateAsync()
        {
            var harness = new InMemoryTestHarness();
            await harness.Start();

            var options = new DbContextOptionsBuilder<DrivingLicenceDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new ControllerFixture(
                harness,
                new DrivingLicenceDbContext(options),
                new ApplicationWorkflowMetrics());
        }

        public async Task<Application> AddApplicationAsync(ApplicationStatus status)
        {
            var now = new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);
            var application = new Application
            {
                Id = Guid.NewGuid(),
                FirstName = "Jane",
                LastName = "Doe",
                DateOfBirth = new DateTime(1990, 1, 1),
                NationalId = Guid.NewGuid().ToString("N"),
                Email = "jane.doe@example.com",
                PhoneNumber = "+30 210 0000000",
                Address = "1 Main Street",
                LicenceCategory = "B",
                PhotoBase64 = "cGhvdG8=",
                Status = status,
                CreatedAt = now,
                UpdatedAt = now,
                AuditTrail =
                [
                    new AuditTrailEntry
                    {
                        Id = Guid.NewGuid(),
                        Timestamp = now,
                        ToStatus = status,
                        Event = "ExistingTransition"
                    }
                ]
            };

            DbContext.Applications.Add(application);
            await DbContext.SaveChangesAsync();
            return application;
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await Harness.Stop();
            _metrics.Dispose();
        }
    }
}
