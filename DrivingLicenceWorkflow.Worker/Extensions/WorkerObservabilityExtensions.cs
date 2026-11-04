using DrivingLicenceWorkflow.Shared.Observability;
using OpenTelemetry.Metrics;

namespace DrivingLicenceWorkflow.Worker.Extensions;

public static class WorkerObservabilityExtensions
{
    /// <summary>
    /// Registers worker tracing and workflow metrics. The worker has no HTTP endpoint, so metrics are pushed
    /// over OTLP when OTEL_EXPORTER_OTLP_ENDPOINT is configured.
    /// </summary>
    public static IServiceCollection AddWorkerObservability(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddSingleton<WorkflowStateMetrics>();
        services.AddWorkflowTracing(configuration, environment, "DrivingLicenceWorkflow.Worker");

        services.AddOpenTelemetry().WithMetrics(metrics =>
        {
            metrics.AddMeter(WorkflowStateMetrics.MeterName);

            if (environment.IsDevelopment())
            {
                metrics.AddConsoleExporter();
            }

            if (!string.IsNullOrWhiteSpace(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
            {
                metrics.AddOtlpExporter();
            }
        });

        return services;
    }
}
