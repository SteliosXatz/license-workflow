using DrivingLicenceWorkflow.Shared.Data;
using DrivingLicenceWorkflow.Shared.Observability;
using DrivingLicenceWorkflow.Shared.Sagas;
using DrivingLicenceWorkflow.Shared.Services;
using DrivingLicenceWorkflow.Worker.Extensions;
using DrivingLicenceWorkflow.Worker.Services;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DrivingLicenceWorkflow.Tests.Worker;

public sealed class WorkerDependencyInjectionTests
{
    [Fact]
    public void Saga_state_machine_can_be_resolved_with_scoped_audit_service()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RabbitMQ:Host"] = "localhost",
                ["RabbitMQ:Username"] = "test",
                ["RabbitMQ:Password"] = "test"
            })
            .Build();

        var services = new ServiceCollection()
            .AddLogging()
            .AddDbContext<DrivingLicenceDbContext>(options =>
                options.UseNpgsql("Host=localhost;Database=workflow;Username=test;Password=test"))
            .AddSingleton<TimeProvider>(TimeProvider.System)
            .AddSingleton<WorkflowStateMetrics>()
            .AddScoped<IAuditService, AuditService>()
            .AddScoped(typeof(WorkflowAuditActivity<>));

        services.AddWorkflowMessaging(configuration);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true
        });

        Assert.NotNull(provider.GetRequiredService<ApplicationWorkflowStateMachine>());
    }
}
