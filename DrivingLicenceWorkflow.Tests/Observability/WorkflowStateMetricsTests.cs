using System.Diagnostics.Metrics;
using DrivingLicenceWorkflow.Shared.Domain;
using DrivingLicenceWorkflow.Shared.Observability;

namespace DrivingLicenceWorkflow.Tests.Observability;

public sealed class WorkflowStateMetricsTests
{
    [Fact]
    public void Records_transitions_with_from_and_to_tags()
    {
        using var metrics = new WorkflowStateMetrics();
        using var listener = new MeterListener();
        var transitions = new List<(string From, string To)>();

        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == WorkflowStateMetrics.MeterName)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            var values = tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value?.ToString() ?? string.Empty);
            transitions.Add((values["from"], values["to"]));
        });
        listener.Start();

        metrics.RecordTransition(null, ApplicationStatus.Submitted);
        metrics.RecordTransition(ApplicationStatus.RiskAssessment, ApplicationStatus.PendingManualReview);

        Assert.Equal([("None", "Submitted"), ("RiskAssessment", "PendingManualReview")], transitions);
    }
}
