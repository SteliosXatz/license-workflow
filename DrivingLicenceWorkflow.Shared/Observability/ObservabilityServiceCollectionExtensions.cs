using MassTransit.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace DrivingLicenceWorkflow.Shared.Observability;

public static class ObservabilityServiceCollectionExtensions
{
    private const double ProductionSamplingRatio = 0.1;

    /// <summary>
    /// Registers OpenTelemetry tracing for MassTransit and EF Core. Development uses the default console
    /// exporter; OTLP export is enabled by OTEL_EXPORTER_OTLP_ENDPOINT.
    /// </summary>
    public static IServiceCollection AddWorkflowTracing(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        string serviceName,
        Action<TracerProviderBuilder>? configure = null)
    {
        services.AddOpenTelemetry().WithTracing(tracing =>
        {
            tracing
                .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(serviceName))
                .SetSampler(environment.IsDevelopment()
                    ? new AlwaysOnSampler()
                    : new TraceIdRatioBasedSampler(ProductionSamplingRatio))
                .AddSource(DiagnosticHeaders.DefaultListenerName)
                .AddEntityFrameworkCoreInstrumentation();

            configure?.Invoke(tracing);

            if (environment.IsDevelopment())
            {
                tracing.AddConsoleExporter();
            }

            if (!string.IsNullOrWhiteSpace(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
            {
                tracing.AddOtlpExporter();
            }
        });

        return services;
    }
}
