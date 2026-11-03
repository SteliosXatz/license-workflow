using MassTransit;

namespace DrivingLicenceWorkflow.Shared.Domain;

public class ApplicationWorkflowState : SagaStateMachineInstance
{
    public Guid CorrelationId { get; set; }
    public string CurrentState { get; set; } = string.Empty;
    public Guid ApplicationId { get; set; }
    public int? PhotoQualityScore { get; set; }
    public string? FailureReason { get; set; }
    public string? RejectionReason { get; set; }
}
