using System.Diagnostics.Metrics;
using DrivingLicenceWorkflow.Shared.Domain;

namespace DrivingLicenceWorkflow.Shared.Observability;

/// <summary>Showcase metric: counts workflow state transitions applied by the saga.</summary>
public sealed class WorkflowStateMetrics : IDisposable
{
    public const string MeterName = "DrivingLicenceWorkflow.Worker";

    private readonly Meter _meter = new(MeterName);
    private readonly Counter<long> _transitions;

    public WorkflowStateMetrics() =>
        _transitions = _meter.CreateCounter<long>(
            "driving_licence_workflow_transitions",
            description: "The number of workflow state transitions.");

    public void RecordTransition(ApplicationStatus? from, ApplicationStatus to) =>
        _transitions.Add(
            1,
            new KeyValuePair<string, object?>("from", from?.ToString() ?? "None"),
            new KeyValuePair<string, object?>("to", to.ToString()));

    public void Dispose() => _meter.Dispose();
}
