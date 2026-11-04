using System.Diagnostics.Metrics;

namespace DrivingLicenceWorkflow.Api.Observability;

public sealed class ApplicationWorkflowMetrics : IDisposable
{
    public const string MeterName = "DrivingLicenceWorkflow.Api";

    private readonly Meter _meter = new(MeterName);
    private readonly Counter<long> _applicationsSubmitted;

    public ApplicationWorkflowMetrics() =>
        _applicationsSubmitted = _meter.CreateCounter<long>(
            "driving_licence_applications_submitted",
            description: "The number of applications submitted to the workflow.");

    public void RecordApplicationSubmitted() => _applicationsSubmitted.Add(1);

    public void Dispose() => _meter.Dispose();
}
