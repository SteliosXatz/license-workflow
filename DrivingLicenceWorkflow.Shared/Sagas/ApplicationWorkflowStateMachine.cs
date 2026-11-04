using DrivingLicenceWorkflow.Shared.Domain;
using DrivingLicenceWorkflow.Shared.Messages;
using DrivingLicenceWorkflow.Shared.Services;
using MassTransit;

namespace DrivingLicenceWorkflow.Shared.Sagas;

public class ApplicationWorkflowStateMachine : MassTransitStateMachine<ApplicationWorkflowState>
{
    private readonly IAuditService _auditService;

    public State Submitted { get; private set; } = null!;
    public State ValidatingData { get; private set; } = null!;
    public State CheckingPhoto { get; private set; } = null!;
    public State RiskAssessment { get; private set; } = null!;
    public State PendingManualReview { get; private set; } = null!;
    public State Approved { get; private set; } = null!;
    public State Rejected { get; private set; } = null!;
    public State Failed { get; private set; } = null!;

    public Event<ApplicationSubmitted> ApplicationSubmitted { get; private set; } = null!;
    public Event<ApplicationValidated> ApplicationValidated { get; private set; } = null!;
    public Event<ApplicationValidationFailed> ApplicationValidationFailed { get; private set; } = null!;
    public Event<PhotoChecked> PhotoChecked { get; private set; } = null!;
    public Event<PhotoCheckFailed> PhotoCheckFailed { get; private set; } = null!;
    public Event<RiskApproved> RiskApproved { get; private set; } = null!;
    public Event<RiskManualReviewRequired> RiskManualReviewRequired { get; private set; } = null!;
    public Event<ManualReviewApproved> ManualReviewApproved { get; private set; } = null!;
    public Event<ManualReviewRejected> ManualReviewRejected { get; private set; } = null!;

    public ApplicationWorkflowStateMachine(IAuditService auditService)
    {
        _auditService = auditService;

        InstanceState(state => state.CurrentState);
        SetCompletedWhenFinalized();

        Event(() => ApplicationSubmitted, e => e.CorrelateById(context => context.Message.ApplicationId));
        Event(() => ApplicationValidated, e => e.CorrelateById(context => context.Message.ApplicationId));
        Event(() => ApplicationValidationFailed, e => e.CorrelateById(context => context.Message.ApplicationId));
        Event(() => PhotoChecked, e => e.CorrelateById(context => context.Message.ApplicationId));
        Event(() => PhotoCheckFailed, e => e.CorrelateById(context => context.Message.ApplicationId));
        Event(() => RiskApproved, e => e.CorrelateById(context => context.Message.ApplicationId));
        Event(() => RiskManualReviewRequired, e => e.CorrelateById(context => context.Message.ApplicationId));
        Event(() => ManualReviewApproved, e => e.CorrelateById(context => context.Message.ApplicationId));
        Event(() => ManualReviewRejected, e => e.CorrelateById(context => context.Message.ApplicationId));

        Initially(
            When(ApplicationSubmitted)
                .Then(context =>
                {
                    context.Saga.CorrelationId = context.Message.ApplicationId;
                    context.Saga.ApplicationId = context.Message.ApplicationId;
                })
                .ThenAsync(context => RecordTransitionAsync(
                    context,
                    ApplicationStatus.Submitted,
                    ApplicationStatus.ValidatingData,
                    nameof(ApplicationSubmitted),
                    null))
                .TransitionTo(ValidatingData)
                .Send(context => context.Init<ValidateApplication>(
                    new ValidateApplication(context.Saga.ApplicationId))));

        During(ValidatingData,
            When(ApplicationValidated)
                .ThenAsync(context => RecordTransitionAsync(
                    context,
                    ApplicationStatus.ValidatingData,
                    ApplicationStatus.CheckingPhoto,
                    nameof(ApplicationValidated),
                    null))
                .TransitionTo(CheckingPhoto)
                .Send(context => context.Init<CheckPhoto>(
                    new CheckPhoto(context.Saga.ApplicationId))),

            When(ApplicationValidationFailed)
                .Then(context => context.Saga.FailureReason = context.Message.Reason)
                .ThenAsync(context => RecordTransitionAsync(
                    context,
                    ApplicationStatus.ValidatingData,
                    ApplicationStatus.Failed,
                    nameof(ApplicationValidationFailed),
                    context.Message.Reason))
                .TransitionTo(Failed)
                .Finalize());

        During(CheckingPhoto,
            When(PhotoChecked)
                .Then(context => context.Saga.PhotoQualityScore = context.Message.QualityScore)
                .ThenAsync(context => RecordTransitionAsync(
                    context,
                    ApplicationStatus.CheckingPhoto,
                    ApplicationStatus.RiskAssessment,
                    nameof(PhotoChecked),
                    null))
                .TransitionTo(RiskAssessment)
                .Send(context => context.Init<AssessRisk>(
                    new AssessRisk(context.Saga.ApplicationId))),

            When(PhotoCheckFailed)
                .Then(context => context.Saga.FailureReason = context.Message.Reason)
                .ThenAsync(context => RecordTransitionAsync(
                    context,
                    ApplicationStatus.CheckingPhoto,
                    ApplicationStatus.Failed,
                    nameof(PhotoCheckFailed),
                    context.Message.Reason))
                .TransitionTo(Failed)
                .Finalize());

        During(RiskAssessment,
            When(RiskApproved)
                .ThenAsync(context => RecordTransitionAsync(
                    context,
                    ApplicationStatus.RiskAssessment,
                    ApplicationStatus.Approved,
                    nameof(RiskApproved),
                    null))
                .TransitionTo(Approved)
                .Finalize(),

            When(RiskManualReviewRequired)
                .Then(context => context.Saga.FailureReason = context.Message.Reason)
                .ThenAsync(context => RecordTransitionAsync(
                    context,
                    ApplicationStatus.RiskAssessment,
                    ApplicationStatus.PendingManualReview,
                    nameof(RiskManualReviewRequired),
                    context.Message.Reason))
                .TransitionTo(PendingManualReview));

        During(PendingManualReview,
            When(ManualReviewApproved)
                .ThenAsync(context => RecordTransitionAsync(
                    context,
                    ApplicationStatus.PendingManualReview,
                    ApplicationStatus.Approved,
                    nameof(ManualReviewApproved),
                    null))
                .TransitionTo(Approved)
                .Finalize(),

            When(ManualReviewRejected)
                .Then(context => context.Saga.RejectionReason = context.Message.RejectionReason)
                .ThenAsync(context => RecordTransitionAsync(
                    context,
                    ApplicationStatus.PendingManualReview,
                    ApplicationStatus.Rejected,
                    nameof(ManualReviewRejected),
                    context.Message.RejectionReason))
                .TransitionTo(Rejected)
                .Finalize());
    }

    private Task RecordTransitionAsync<TMessage>(
        BehaviorContext<ApplicationWorkflowState, TMessage> context,
        ApplicationStatus? fromStatus,
        ApplicationStatus toStatus,
        string eventName,
        string? details)
        where TMessage : class =>
        _auditService.RecordTransitionAsync(
            context.Saga.ApplicationId,
            fromStatus,
            toStatus,
            eventName,
            details,
            context.CancellationToken);
}
