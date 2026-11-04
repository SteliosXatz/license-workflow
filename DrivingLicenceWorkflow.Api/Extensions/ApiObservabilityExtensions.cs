using DrivingLicenceWorkflow.Api.Observability;
using DrivingLicenceWorkflow.Shared.Observability;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace DrivingLicenceWorkflow.Api.Extensions;

public static class ApiObservabilityExtensions
{
    public static IServiceCollection AddApiObservability(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddWorkflowTracing(
            configuration,
            environment,
            "DrivingLicenceWorkflow.Api",
            tracing => tracing.AddAspNetCoreInstrumentation());

        services.AddOpenTelemetry().WithMetrics(metrics => metrics
            .AddMeter(ApplicationWorkflowMetrics.MeterName)
            .AddAspNetCoreInstrumentation()
            .AddPrometheusExporter());

        return services;
    }
}
