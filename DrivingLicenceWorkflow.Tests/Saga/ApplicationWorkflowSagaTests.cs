using DrivingLicenceWorkflow.Shared.Domain;
using DrivingLicenceWorkflow.Shared.Messages;
using DrivingLicenceWorkflow.Shared.Observability;
using DrivingLicenceWorkflow.Shared.Sagas;
using DrivingLicenceWorkflow.Shared.Services;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DrivingLicenceWorkflow.Tests.Saga;

public sealed class ApplicationWorkflowSagaTests
{
    private static readonly ApplicationStatus[][] SuccessfulPath =
    [
        [ApplicationStatus.Submitted, ApplicationStatus.ValidatingData],
        [ApplicationStatus.ValidatingData, ApplicationStatus.CheckingPhoto],
        [ApplicationStatus.CheckingPhoto, ApplicationStatus.RiskAssessment],
        [ApplicationStatus.RiskAssessment, ApplicationStatus.Approved]
    ];

    [Theory]
    [InlineData(WorkflowScenario.Approval)]
    [InlineData(WorkflowScenario.ValidationFailure)]
    [InlineData(WorkflowScenario.ManualApproval)]
    [InlineData(WorkflowScenario.ManualRejection)]
    public async Task Processes_expected_workflow_paths(WorkflowScenario scenario)
    {
        var (provider, audit) = CreateHarness();
        await using (provider)
        {
            var harness = provider.GetRequiredService<ITestHarness>();
            await harness.Start();
            var applicationId = Guid.NewGuid();

            await harness.Bus.Publish(new ApplicationSubmitted(applicationId));
            Assert.True(await harness.Consumed.Any<ApplicationSubmitted>(
                item => item.Context.Message.ApplicationId == applicationId));

            var expectedStatus = scenario switch
            {
                WorkflowScenario.Approval or WorkflowScenario.ManualApproval => ApplicationStatus.Approved,
                WorkflowScenario.ManualRejection => ApplicationStatus.Rejected,
                WorkflowScenario.ValidationFailure => ApplicationStatus.Failed,
                _ => throw new ArgumentOutOfRangeException(nameof(scenario))
            };

            Assert.True(WaitForTransition(audit, applicationId, ApplicationStatus.ValidatingData));
            var sagaHarness = harness.GetSagaStateMachineHarness<
                ApplicationWorkflowStateMachine,
                ApplicationWorkflowState>();
            var saga = sagaHarness.Created.ContainsInState(
                applicationId,
                sagaHarness.StateMachine,
                sagaHarness.StateMachine.ValidatingData);
            Assert.NotNull(saga);
            Assert.True(await harness.Sent.Any<ValidateApplication>());
            Assert.True(await harness.Consumed.Any<ValidateApplication>());

            if (scenario == WorkflowScenario.ValidationFailure)
            {
                await harness.Bus.Publish(new ApplicationValidationFailed(applicationId, "Validation failed."));
                Assert.True(await harness.Consumed.Any<ApplicationValidationFailed>(
                    item => item.Context.Message.ApplicationId == applicationId));
                Assert.True(WaitForTransition(audit, applicationId, ApplicationStatus.Failed));
                Assert.Contains(audit.For(applicationId), transition =>
                    transition.ToStatus == ApplicationStatus.Failed
                    && transition.Details == "Validation failed.");
                Assert.DoesNotContain(audit.For(applicationId), transition => transition.ToStatus == ApplicationStatus.CheckingPhoto);
                Assert.Equal(0, await CountSentAsync<CheckPhoto>(harness));
                return;
            }

            await harness.Bus.Publish(new ApplicationValidated(applicationId));
            Assert.True(await harness.Consumed.Any<ApplicationValidated>(
                item => item.Context.Message.ApplicationId == applicationId));
            Assert.True(WaitForTransition(audit, applicationId, ApplicationStatus.CheckingPhoto));
            await harness.Bus.Publish(new PhotoChecked(applicationId, 80));
            Assert.True(await harness.Consumed.Any<PhotoChecked>(
                item => item.Context.Message.ApplicationId == applicationId));
            Assert.True(WaitForTransition(audit, applicationId, ApplicationStatus.RiskAssessment));

            if (scenario == WorkflowScenario.Approval)
            {
                await harness.Bus.Publish(new RiskApproved(applicationId));
            }
            else
            {
                await harness.Bus.Publish(new RiskManualReviewRequired(applicationId, "Manual review required."));
                Assert.True(await harness.Consumed.Any<RiskManualReviewRequired>(
                    item => item.Context.Message.ApplicationId == applicationId));
                Assert.True(WaitForTransition(audit, applicationId, ApplicationStatus.PendingManualReview));
                await harness.Bus.Publish(scenario == WorkflowScenario.ManualApproval
                    ? new ManualReviewApproved(applicationId)
                    : new ManualReviewRejected(applicationId, "Test rejection"));
            }

            Assert.True(WaitForTransition(audit, applicationId, expectedStatus));
            Assert.Equal(expectedStatus, audit.For(applicationId).Last().ToStatus);
            Assert.Contains(audit.For(applicationId), transition =>
                transition.ToStatus == expectedStatus && !string.IsNullOrWhiteSpace(transition.Event));
        }
    }

    [Fact]
    // Feature: driving-licence-workflow, Property 6: State machine transition invariant
    public async Task Accepts_only_the_defined_state_and_event_pairs()
    {
        var (provider, audit) = CreateHarness();
        await using (provider)
        {
            var harness = provider.GetRequiredService<ITestHarness>();
            await harness.Start();

            var states = Enum.GetValues<ApplicationStatus>();
            var events = new Func<Guid, object>[]
            {
                id => new ApplicationSubmitted(id),
                id => new ApplicationValidated(id),
                id => new ApplicationValidationFailed(id, "invalid"),
                id => new PhotoChecked(id, 80),
                id => new PhotoCheckFailed(id, "invalid photo"),
                id => new RiskApproved(id),
                id => new RiskManualReviewRequired(id, "review"),
                id => new ManualReviewApproved(id),
                id => new ManualReviewRejected(id, "rejected")
            };

            foreach (var state in states)
            {
                foreach (var makeEvent in events)
                {
                    var applicationId = Guid.NewGuid();
                    await PutSagaInStateAsync(harness, applicationId, state);
                    var before = audit.For(applicationId).Length;
                    var message = makeEvent(applicationId);

                    await harness.Bus.Publish(message);
                    await WaitForConsumedAsync(harness, message);

                    var shouldTransition = IsDefinedTransition(state, message);
                    Assert.Equal(before + (shouldTransition ? 1 : 0), audit.For(applicationId).Length);
                }
            }
        }
    }

    [Fact]
    // Feature: driving-licence-workflow, Property 9: Idempotent duplicate message handling
    public async Task Duplicate_event_after_state_progression_does_not_repeat_transition_or_command()
    {
        var (provider, audit) = CreateHarness();
        await using (provider)
        {
            var harness = provider.GetRequiredService<ITestHarness>();
            await harness.Start();
            var applicationId = Guid.NewGuid();
            var submitted = new ApplicationSubmitted(applicationId);

            await harness.Bus.Publish(submitted);
            Assert.True(WaitForTransition(audit, applicationId, ApplicationStatus.ValidatingData));
            var transitionCount = audit.For(applicationId).Length;
            Assert.True(await harness.Sent.Any<ValidateApplication>());
            var validationCommandCount = await CountSentAsync<ValidateApplication>(harness);
            Assert.Equal(1, validationCommandCount);
            await harness.Bus.Publish(submitted);
            await harness.InactivityTask;

            Assert.Equal(transitionCount, audit.For(applicationId).Length);
            Assert.Equal(validationCommandCount, await CountSentAsync<ValidateApplication>(harness));
        }
    }

    [Fact]
    // Feature: driving-licence-workflow, Property 11: Step failure recording
    public async Task Explicit_step_failure_is_audited_and_does_not_advance_the_saga()
    {
        var (provider, audit) = CreateHarness();
        await using (provider)
        {
            var harness = provider.GetRequiredService<ITestHarness>();
            await harness.Start();
            var applicationId = Guid.NewGuid();

            await harness.Bus.Publish(new ApplicationSubmitted(applicationId));
            Assert.True(await harness.Consumed.Any<ApplicationSubmitted>(
                item => item.Context.Message.ApplicationId == applicationId));
            await harness.Bus.Publish(new ApplicationValidationFailed(applicationId, "Validation failed."));

            Assert.True(WaitForTransition(audit, applicationId, ApplicationStatus.Failed));
            Assert.Contains(audit.For(applicationId), transition =>
                transition.ToStatus == ApplicationStatus.Failed
                && transition.Details == "Validation failed.");
            Assert.DoesNotContain(audit.For(applicationId), transition => transition.ToStatus == ApplicationStatus.CheckingPhoto);
            Assert.Equal(0, await CountSentAsync<CheckPhoto>(harness));
        }
    }

    [Theory]
    [InlineData(ThrowingWorkflowStep.Validation)]
    [InlineData(ThrowingWorkflowStep.Photo)]
    [InlineData(ThrowingWorkflowStep.Risk)]
    // Feature: driving-licence-workflow, Property 11: Step failure recording
    public async Task Unexpected_step_exception_is_audited_and_does_not_advance_the_saga(
        ThrowingWorkflowStep throwingStep)
    {
        var (provider, audit) = CreateHarness(throwingStep);
        await using (provider)
        {
            var harness = provider.GetRequiredService<ITestHarness>();
            await harness.Start();
            var applicationId = Guid.NewGuid();

            await harness.Bus.Publish(new ApplicationSubmitted(applicationId));

            if (throwingStep is ThrowingWorkflowStep.Photo or ThrowingWorkflowStep.Risk)
            {
                await harness.Bus.Publish(new ApplicationValidated(applicationId));
                Assert.True(await harness.Consumed.Any<ApplicationValidated>(
                    item => item.Context.Message.ApplicationId == applicationId));
                Assert.True(WaitForTransition(audit, applicationId, ApplicationStatus.CheckingPhoto));
            }

            if (throwingStep == ThrowingWorkflowStep.Risk)
            {
                await harness.Bus.Publish(new PhotoChecked(applicationId, 80));
                Assert.True(WaitForTransition(audit, applicationId, ApplicationStatus.RiskAssessment));
            }

            var failureReason = throwingStep switch
            {
                ThrowingWorkflowStep.Validation => "Unexpected validation service failure.",
                ThrowingWorkflowStep.Photo => "Unexpected photo service failure.",
                ThrowingWorkflowStep.Risk => "Unexpected risk service failure.",
                _ => throw new ArgumentOutOfRangeException(nameof(throwingStep))
            };
            var faultConsumed = throwingStep switch
            {
                ThrowingWorkflowStep.Validation => await harness.Consumed.Any<Fault<ValidateApplication>>(
                    item => item.Context.Message.Message.ApplicationId == applicationId),
                ThrowingWorkflowStep.Photo => await harness.Consumed.Any<Fault<CheckPhoto>>(
                    item => item.Context.Message.Message.ApplicationId == applicationId),
                ThrowingWorkflowStep.Risk => await harness.Consumed.Any<Fault<AssessRisk>>(
                    item => item.Context.Message.Message.ApplicationId == applicationId),
                _ => false
            };
            Assert.True(faultConsumed);
            Assert.True(WaitForTransition(audit, applicationId, ApplicationStatus.Failed));

            var failedTransition = Assert.Single(audit.For(applicationId)
                .Where(transition => transition.ToStatus == ApplicationStatus.Failed));
            Assert.Equal(failureReason, failedTransition.Details);
            switch (throwingStep)
            {
                case ThrowingWorkflowStep.Validation:
                    Assert.DoesNotContain(audit.For(applicationId), transition =>
                        transition.ToStatus == ApplicationStatus.CheckingPhoto);
                    Assert.Equal(0, await CountSentAsync<CheckPhoto>(harness));
                    break;
                case ThrowingWorkflowStep.Photo:
                    Assert.DoesNotContain(audit.For(applicationId), transition =>
                        transition.ToStatus == ApplicationStatus.RiskAssessment);
                    Assert.Equal(0, await CountSentAsync<AssessRisk>(harness));
                    break;
                case ThrowingWorkflowStep.Risk:
                    Assert.DoesNotContain(audit.For(applicationId), transition =>
                        transition.ToStatus is ApplicationStatus.PendingManualReview or ApplicationStatus.Approved);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(throwingStep));
            }
        }
    }

    private static (ServiceProvider Provider, SagaAuditService Audit) CreateHarness(
        ThrowingWorkflowStep? throwingStep = null)
    {
        MapCommandEndpoints();
        var audit = new SagaAuditService();
        var provider = new ServiceCollection()
            .AddLogging(logging => logging.AddConsole().SetMinimumLevel(LogLevel.Warning))
            .AddSingleton<IAuditService>(audit)
            .AddSingleton<WorkflowStateMetrics>()
            .AddScoped(typeof(WorkflowAuditActivity<>))
            .AddMassTransitTestHarness(configure =>
            {
                configure.AddSagaStateMachine<ApplicationWorkflowStateMachine, ApplicationWorkflowState>()
                    .InMemoryRepository();
                if (throwingStep == ThrowingWorkflowStep.Validation)
                {
                    configure.AddConsumer<ThrowingValidateApplicationCommandConsumer>()
                        .Endpoint(endpoint => endpoint.Name = "validate-application");
                }
                else
                {
                    configure.AddConsumer<ValidateApplicationCommandConsumer>()
                        .Endpoint(endpoint => endpoint.Name = "validate-application");
                }
                if (throwingStep == ThrowingWorkflowStep.Photo)
                {
                    configure.AddConsumer<ThrowingCheckPhotoCommandConsumer>()
                        .Endpoint(endpoint => endpoint.Name = "check-photo");
                }
                else
                {
                    configure.AddConsumer<CheckPhotoCommandConsumer>()
                        .Endpoint(endpoint => endpoint.Name = "check-photo");
                }

                if (throwingStep == ThrowingWorkflowStep.Risk)
                {
                    configure.AddConsumer<ThrowingAssessRiskCommandConsumer>()
                        .Endpoint(endpoint => endpoint.Name = "assess-risk");
                }
                else
                {
                    configure.AddConsumer<AssessRiskCommandConsumer>()
                        .Endpoint(endpoint => endpoint.Name = "assess-risk");
                }
            })
            .BuildServiceProvider(true);

        return (provider, audit);
    }

    private static async Task<int> CountSentAsync<TMessage>(ITestHarness harness)
        where TMessage : class
    {
        var count = 0;
        foreach (var _ in harness.Sent.Select<TMessage>())
        {
            count++;
        }

        return count;
    }

    private static readonly object EndpointMapLock = new();
    private static bool _commandEndpointsMapped;

    private static void MapCommandEndpoints()
    {
        lock (EndpointMapLock)
        {
            if (_commandEndpointsMapped)
            {
                return;
            }

            EndpointConvention.Map<ValidateApplication>(new Uri("queue:validate-application"));
            EndpointConvention.Map<CheckPhoto>(new Uri("queue:check-photo"));
            EndpointConvention.Map<AssessRisk>(new Uri("queue:assess-risk"));
            _commandEndpointsMapped = true;
        }
    }

    private static bool IsDefinedTransition(ApplicationStatus state, object message) =>
        (state, message) switch
        {
            (ApplicationStatus.Submitted, ApplicationSubmitted) => true,
            (ApplicationStatus.ValidatingData, ApplicationValidated) => true,
            (ApplicationStatus.ValidatingData, ApplicationValidationFailed) => true,
            (ApplicationStatus.CheckingPhoto, PhotoChecked) => true,
            (ApplicationStatus.CheckingPhoto, PhotoCheckFailed) => true,
            (ApplicationStatus.RiskAssessment, RiskApproved) => true,
            (ApplicationStatus.RiskAssessment, RiskManualReviewRequired) => true,
            (ApplicationStatus.PendingManualReview, ManualReviewApproved) => true,
            (ApplicationStatus.PendingManualReview, ManualReviewRejected) => true,
            _ => false
        };

    private static async Task PutSagaInStateAsync(ITestHarness harness, Guid id, ApplicationStatus state)
    {
        if (state == ApplicationStatus.Submitted)
        {
            return;
        }

        await harness.Bus.Publish(new ApplicationSubmitted(id));
        await WaitForConsumedAsync(harness, new ApplicationSubmitted(id));

        if (state == ApplicationStatus.ValidatingData)
        {
            return;
        }

        if (state == ApplicationStatus.Failed)
        {
            await harness.Bus.Publish(new ApplicationValidationFailed(id, "invalid"));
            await WaitForConsumedAsync(harness, new ApplicationValidationFailed(id, "invalid"));
            return;
        }

        await harness.Bus.Publish(new ApplicationValidated(id));
        await WaitForConsumedAsync(harness, new ApplicationValidated(id));

        if (state == ApplicationStatus.CheckingPhoto)
        {
            return;
        }

        if (state == ApplicationStatus.RiskAssessment)
        {
            await harness.Bus.Publish(new PhotoChecked(id, 80));
            await WaitForConsumedAsync(harness, new PhotoChecked(id, 80));
            return;
        }

        await harness.Bus.Publish(new PhotoChecked(id, 80));
        await WaitForConsumedAsync(harness, new PhotoChecked(id, 80));
        if (state == ApplicationStatus.PendingManualReview)
        {
            await harness.Bus.Publish(new RiskManualReviewRequired(id, "review"));
            await WaitForConsumedAsync(harness, new RiskManualReviewRequired(id, "review"));
            return;
        }

        if (state == ApplicationStatus.Approved)
        {
            await harness.Bus.Publish(new RiskApproved(id));
            await WaitForConsumedAsync(harness, new RiskApproved(id));
            return;
        }

        if (state == ApplicationStatus.Rejected)
        {
            await harness.Bus.Publish(new RiskManualReviewRequired(id, "review"));
            await WaitForConsumedAsync(harness, new RiskManualReviewRequired(id, "review"));
            await harness.Bus.Publish(new ManualReviewRejected(id, "rejected"));
            await WaitForConsumedAsync(harness, new ManualReviewRejected(id, "rejected"));
        }
    }

    private static bool WaitForTransition(SagaAuditService audit, Guid id, ApplicationStatus status) =>
        SpinWait.SpinUntil(() => audit.For(id).Any(transition => transition.ToStatus == status), TimeSpan.FromSeconds(10));

    private static async Task WaitForConsumedAsync(ITestHarness harness, object message, int minimumCount = 1)
    {
        var applicationId = (Guid)message.GetType().GetProperty("ApplicationId")!.GetValue(message)!;
        var consumed = message switch
        {
            ApplicationSubmitted => harness.Consumed.Any<ApplicationSubmitted>(
                item => item.Context.Message.ApplicationId == applicationId),
            ApplicationValidated => harness.Consumed.Any<ApplicationValidated>(
                item => item.Context.Message.ApplicationId == applicationId),
            ApplicationValidationFailed => harness.Consumed.Any<ApplicationValidationFailed>(
                item => item.Context.Message.ApplicationId == applicationId),
            PhotoChecked => harness.Consumed.Any<PhotoChecked>(
                item => item.Context.Message.ApplicationId == applicationId),
            PhotoCheckFailed => harness.Consumed.Any<PhotoCheckFailed>(
                item => item.Context.Message.ApplicationId == applicationId),
            RiskApproved => harness.Consumed.Any<RiskApproved>(
                item => item.Context.Message.ApplicationId == applicationId),
            RiskManualReviewRequired => harness.Consumed.Any<RiskManualReviewRequired>(
                item => item.Context.Message.ApplicationId == applicationId),
            ManualReviewApproved => harness.Consumed.Any<ManualReviewApproved>(
                item => item.Context.Message.ApplicationId == applicationId),
            ManualReviewRejected => harness.Consumed.Any<ManualReviewRejected>(
                item => item.Context.Message.ApplicationId == applicationId),
            _ => throw new ArgumentOutOfRangeException(nameof(message))
        };
        Assert.True(await consumed);

        if (minimumCount > 1)
        {
            await Task.Delay(100);
        }
    }

    public enum WorkflowScenario
    {
        Approval,
        ValidationFailure,
        ManualApproval,
        ManualRejection,
        PauseAfterSubmission
    }

    public enum ThrowingWorkflowStep
    {
        Validation,
        Photo,
        Risk
    }

    private sealed class SagaAuditService : IAuditService
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<AuditRecord> _records = new();

        public Task RecordTransitionAsync(
            Guid applicationId,
            ApplicationStatus? fromStatus,
            ApplicationStatus toStatus,
            string eventName,
            string? details,
            CancellationToken cancellationToken = default)
        {
            _records.Enqueue(new AuditRecord(applicationId, fromStatus, toStatus, eventName, details));
            return Task.CompletedTask;
        }

        public AuditRecord[] For(Guid applicationId) =>
            _records.Where(record => record.ApplicationId == applicationId).ToArray();
    }

    private sealed record AuditRecord(
        Guid ApplicationId,
        ApplicationStatus? FromStatus,
        ApplicationStatus ToStatus,
        string Event,
        string? Details);

    private sealed class ValidateApplicationCommandConsumer : IConsumer<ValidateApplication>
    {
        public Task Consume(ConsumeContext<ValidateApplication> context) => Task.CompletedTask;
    }

    private sealed class ThrowingValidateApplicationCommandConsumer : IConsumer<ValidateApplication>
    {
        public Task Consume(ConsumeContext<ValidateApplication> context) =>
            throw new InvalidOperationException("Unexpected validation service failure.");
    }

    private sealed class CheckPhotoCommandConsumer : IConsumer<CheckPhoto>
    {
        public Task Consume(ConsumeContext<CheckPhoto> context) => Task.CompletedTask;
    }

    private sealed class ThrowingCheckPhotoCommandConsumer : IConsumer<CheckPhoto>
    {
        public Task Consume(ConsumeContext<CheckPhoto> context) =>
            throw new InvalidOperationException("Unexpected photo service failure.");
    }

    private sealed class AssessRiskCommandConsumer : IConsumer<AssessRisk>
    {
        public Task Consume(ConsumeContext<AssessRisk> context) => Task.CompletedTask;
    }

    private sealed class ThrowingAssessRiskCommandConsumer : IConsumer<AssessRisk>
    {
        public Task Consume(ConsumeContext<AssessRisk> context) =>
            throw new InvalidOperationException("Unexpected risk service failure.");
    }

}
