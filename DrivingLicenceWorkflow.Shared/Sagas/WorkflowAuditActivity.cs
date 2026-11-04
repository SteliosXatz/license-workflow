using DrivingLicenceWorkflow.Shared.Domain;
using DrivingLicenceWorkflow.Shared.Messages;
using DrivingLicenceWorkflow.Shared.Services;
using MassTransit;

namespace DrivingLicenceWorkflow.Shared.Sagas;

public sealed class WorkflowAuditActivity<TMessage> : IStateMachineActivity<ApplicationWorkflowState, TMessage>
    where TMessage : class
{
    private readonly IAuditService _auditService;

    public WorkflowAuditActivity(IAuditService auditService)
    {
        _auditService = auditService;
    }

    public async Task Execute(
        BehaviorContext<ApplicationWorkflowState, TMessage> context,
        IBehavior<ApplicationWorkflowState, TMessage> next)
    {
        var (fromStatus, toStatus, eventName, details) = context.Message switch
        {
            ApplicationSubmitted =>
                (ApplicationStatus.Submitted, ApplicationStatus.ValidatingData, nameof(ApplicationSubmitted), null),
            ApplicationValidated =>
                (ApplicationStatus.ValidatingData, ApplicationStatus.CheckingPhoto, nameof(ApplicationValidated), null),
            ApplicationValidationFailed message =>
                (ApplicationStatus.ValidatingData, ApplicationStatus.Failed, nameof(ApplicationValidationFailed), message.Reason),
            Fault<ValidateApplication> fault =>
                (ApplicationStatus.ValidatingData, ApplicationStatus.Failed, "ValidateApplicationFaulted", GetFaultReason(fault)),
            PhotoChecked =>
                (ApplicationStatus.CheckingPhoto, ApplicationStatus.RiskAssessment, nameof(PhotoChecked), null),
            PhotoCheckFailed message =>
                (ApplicationStatus.CheckingPhoto, ApplicationStatus.Failed, nameof(PhotoCheckFailed), message.Reason),
            Fault<CheckPhoto> fault =>
                (ApplicationStatus.CheckingPhoto, ApplicationStatus.Failed, "CheckPhotoFaulted", GetFaultReason(fault)),
            RiskApproved =>
                (ApplicationStatus.RiskAssessment, ApplicationStatus.Approved, nameof(RiskApproved), null),
            RiskManualReviewRequired message =>
                (ApplicationStatus.RiskAssessment, ApplicationStatus.PendingManualReview, nameof(RiskManualReviewRequired), message.Reason),
            Fault<AssessRisk> fault =>
                (ApplicationStatus.RiskAssessment, ApplicationStatus.Failed, "AssessRiskFaulted", GetFaultReason(fault)),
            ManualReviewApproved => (
                ApplicationStatus.PendingManualReview,
                ApplicationStatus.Approved,
                nameof(ManualReviewApproved),
                null),
            ManualReviewRejected message =>
                (ApplicationStatus.PendingManualReview, ApplicationStatus.Rejected, nameof(ManualReviewRejected), message.RejectionReason),
            _ => throw new ArgumentOutOfRangeException(nameof(context), typeof(TMessage).Name, "Unsupported workflow event.")
        };

        await _auditService.RecordTransitionAsync(
            context.Saga.ApplicationId,
            fromStatus,
            toStatus,
            eventName,
            details,
            context.CancellationToken);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<ApplicationWorkflowState, TMessage, TException> context,
        IBehavior<ApplicationWorkflowState, TMessage> next)
        where TException : Exception =>
        next.Faulted(context);

    public void Probe(ProbeContext context)
    {
        context.CreateScope("workflow-audit");
    }

    public void Accept(StateMachineVisitor visitor)
    {
    }

    private static string GetFaultReason<TFaultedMessage>(Fault<TFaultedMessage> fault)
        where TFaultedMessage : class =>
        fault.Exceptions.FirstOrDefault()?.Message
        ?? $"{typeof(TFaultedMessage).Name} step failed unexpectedly.";
}
