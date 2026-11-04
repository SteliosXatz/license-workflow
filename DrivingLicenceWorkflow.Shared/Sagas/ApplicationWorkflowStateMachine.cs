using DrivingLicenceWorkflow.Shared.Domain;
using DrivingLicenceWorkflow.Shared.Messages;
using DrivingLicenceWorkflow.Shared.Observability;
using DrivingLicenceWorkflow.Shared.Services;
using MassTransit;

namespace DrivingLicenceWorkflow.Shared.Sagas;

public class ApplicationWorkflowStateMachine : MassTransitStateMachine<ApplicationWorkflowState>
{
    private readonly WorkflowStateMetrics _metrics;

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
    public Event<Fault<ValidateApplication>> ValidateApplicationFaulted { get; private set; } = null!;
    public Event<PhotoChecked> PhotoChecked { get; private set; } = null!;
    public Event<PhotoCheckFailed> PhotoCheckFailed { get; private set; } = null!;
    public Event<Fault<CheckPhoto>> CheckPhotoFaulted { get; private set; } = null!;
    public Event<RiskApproved> RiskApproved { get; private set; } = null!;
    public Event<RiskManualReviewRequired> RiskManualReviewRequired { get; private set; } = null!;
    public Event<Fault<AssessRisk>> AssessRiskFaulted { get; private set; } = null!;
    public Event<ManualReviewApproved> ManualReviewApproved { get; private set; } = null!;
    public Event<ManualReviewRejected> ManualReviewRejected { get; private set; } = null!;

    public ApplicationWorkflowStateMachine(WorkflowStateMetrics metrics)
    {
        _metrics = metrics;

        InstanceState(state => state.CurrentState);
        SetCompletedWhenFinalized();

        Event(() => ApplicationSubmitted, e => e.CorrelateById(context => context.Message.ApplicationId));
        Event(() => ApplicationValidated, e => e.CorrelateById(context => context.Message.ApplicationId));
        Event(() => ApplicationValidationFailed, e => e.CorrelateById(context => context.Message.ApplicationId));
        Event(() => ValidateApplicationFaulted, e => e.CorrelateById(context => context.Message.Message.ApplicationId));
        Event(() => PhotoChecked, e => e.CorrelateById(context => context.Message.ApplicationId));
        Event(() => PhotoCheckFailed, e => e.CorrelateById(context => context.Message.ApplicationId));
        Event(() => CheckPhotoFaulted, e => e.CorrelateById(context => context.Message.Message.ApplicationId));
        Event(() => RiskApproved, e => e.CorrelateById(context => context.Message.ApplicationId));
        Event(() => RiskManualReviewRequired, e => e.CorrelateById(context => context.Message.ApplicationId));
        Event(() => AssessRiskFaulted, e => e.CorrelateById(context => context.Message.Message.ApplicationId));
        Event(() => ManualReviewApproved, e => e.CorrelateById(context => context.Message.ApplicationId));
        Event(() => ManualReviewRejected, e => e.CorrelateById(context => context.Message.ApplicationId));

        Initially(
            When(ApplicationSubmitted)
                .Then(context =>
                {
                    context.Saga.CorrelationId = context.Message.ApplicationId;
                    context.Saga.ApplicationId = context.Message.ApplicationId;
                })
                .Then(_ => _metrics.RecordTransition(
                    ApplicationStatus.Submitted,
                    ApplicationStatus.ValidatingData))
                .Activity(activity => activity.OfType<WorkflowAuditActivity<ApplicationSubmitted>>())
                .TransitionTo(ValidatingData)
                .Send(context => new ValidateApplication(context.Saga.ApplicationId)));

        During(ValidatingData, Ignore(ApplicationSubmitted));
        During(CheckingPhoto, Ignore(ApplicationSubmitted));
        During(RiskAssessment, Ignore(ApplicationSubmitted));
        During(PendingManualReview, Ignore(ApplicationSubmitted));

        During(ValidatingData,
            When(ApplicationValidated)
                .Then(_ => _metrics.RecordTransition(
                    ApplicationStatus.ValidatingData,
                    ApplicationStatus.CheckingPhoto))
                .Activity(activity => activity.OfType<WorkflowAuditActivity<ApplicationValidated>>())
                .TransitionTo(CheckingPhoto)
                .Send(context => new CheckPhoto(context.Saga.ApplicationId)),

            When(ApplicationValidationFailed)
                .Then(context => context.Saga.FailureReason = context.Message.Reason)
                .Then(_ => _metrics.RecordTransition(
                    ApplicationStatus.ValidatingData,
                    ApplicationStatus.Failed))
                .Activity(activity => activity.OfType<WorkflowAuditActivity<ApplicationValidationFailed>>())
                .TransitionTo(Failed)
                .Finalize(),

            When(ValidateApplicationFaulted)
                .Then(context => context.Saga.FailureReason = GetFaultReason(context.Message))
                .Then(_ => _metrics.RecordTransition(
                    ApplicationStatus.ValidatingData,
                    ApplicationStatus.Failed))
                .Activity(activity => activity.OfType<WorkflowAuditActivity<Fault<ValidateApplication>>>())
                .TransitionTo(Failed)
                .Finalize());

        During(CheckingPhoto,
            When(PhotoChecked)
                .Then(context => context.Saga.PhotoQualityScore = context.Message.QualityScore)
                .Then(_ => _metrics.RecordTransition(
                    ApplicationStatus.CheckingPhoto,
                    ApplicationStatus.RiskAssessment))
                .Activity(activity => activity.OfType<WorkflowAuditActivity<PhotoChecked>>())
                .TransitionTo(RiskAssessment)
                .Send(context => new AssessRisk(context.Saga.ApplicationId)),

            When(PhotoCheckFailed)
                .Then(context => context.Saga.FailureReason = context.Message.Reason)
                .Then(_ => _metrics.RecordTransition(
                    ApplicationStatus.CheckingPhoto,
                    ApplicationStatus.Failed))
                .Activity(activity => activity.OfType<WorkflowAuditActivity<PhotoCheckFailed>>())
                .TransitionTo(Failed)
                .Finalize(),

            When(CheckPhotoFaulted)
                .Then(context => context.Saga.FailureReason = GetFaultReason(context.Message))
                .Then(_ => _metrics.RecordTransition(
                    ApplicationStatus.CheckingPhoto,
                    ApplicationStatus.Failed))
                .Activity(activity => activity.OfType<WorkflowAuditActivity<Fault<CheckPhoto>>>())
                .TransitionTo(Failed)
                .Finalize());

        During(RiskAssessment,
            When(RiskApproved)
                .Then(_ => _metrics.RecordTransition(
                    ApplicationStatus.RiskAssessment,
                    ApplicationStatus.Approved))
                .Activity(activity => activity.OfType<WorkflowAuditActivity<RiskApproved>>())
                .TransitionTo(Approved)
                .Finalize(),

            When(RiskManualReviewRequired)
                .Then(context => context.Saga.FailureReason = context.Message.Reason)
                .Then(_ => _metrics.RecordTransition(
                    ApplicationStatus.RiskAssessment,
                    ApplicationStatus.PendingManualReview))
                .Activity(activity => activity.OfType<WorkflowAuditActivity<RiskManualReviewRequired>>())
                .TransitionTo(PendingManualReview),

            When(AssessRiskFaulted)
                .Then(context => context.Saga.FailureReason = GetFaultReason(context.Message))
                .Then(_ => _metrics.RecordTransition(
                    ApplicationStatus.RiskAssessment,
                    ApplicationStatus.Failed))
                .Activity(activity => activity.OfType<WorkflowAuditActivity<Fault<AssessRisk>>>())
                .TransitionTo(Failed)
                .Finalize());

        During(PendingManualReview,
            When(ManualReviewApproved)
                .Then(_ => _metrics.RecordTransition(
                    ApplicationStatus.PendingManualReview,
                    ApplicationStatus.Approved))
                .Activity(activity => activity.OfType<WorkflowAuditActivity<ManualReviewApproved>>())
                .TransitionTo(Approved)
                .Finalize(),

            When(ManualReviewRejected)
                .Then(context => context.Saga.RejectionReason = context.Message.RejectionReason)
                .Then(_ => _metrics.RecordTransition(
                    ApplicationStatus.PendingManualReview,
                    ApplicationStatus.Rejected))
                .Activity(activity => activity.OfType<WorkflowAuditActivity<ManualReviewRejected>>())
                .TransitionTo(Rejected)
                .Finalize());
    }

    private static string GetFaultReason<TMessage>(Fault<TMessage> fault)
        where TMessage : class =>
        fault.Exceptions.FirstOrDefault()?.Message
        ?? $"{typeof(TMessage).Name} step failed unexpectedly.";
}
