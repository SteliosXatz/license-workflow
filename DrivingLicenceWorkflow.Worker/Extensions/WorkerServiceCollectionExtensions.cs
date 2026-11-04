using DrivingLicenceWorkflow.Shared.Data;
using DrivingLicenceWorkflow.Shared.Domain;
using DrivingLicenceWorkflow.Shared.Exceptions;
using DrivingLicenceWorkflow.Shared.Messages;
using DrivingLicenceWorkflow.Shared.Sagas;
using DrivingLicenceWorkflow.Shared.Services;
using DrivingLicenceWorkflow.Worker.Consumers;
using DrivingLicenceWorkflow.Worker.Logging;
using DrivingLicenceWorkflow.Worker.Services;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace DrivingLicenceWorkflow.Worker.Extensions;

public static class WorkerServiceCollectionExtensions
{
    public static IServiceCollection AddWorkerServices(this IServiceCollection services, IConfiguration configuration)
    {
        var workerDbConnectionString = configuration.GetConnectionString("WorkerDb")
            ?? throw new InvalidOperationException("ConnectionStrings:WorkerDb is not configured.");

        services.AddDbContext<DrivingLicenceDbContext>(options =>
            options.UseNpgsql(workerDbConnectionString));
        services.AddBusinessRuleServices(configuration);
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped(typeof(WorkflowAuditActivity<>));
        services.AddConsumeObserver<WorkflowMessageObserver>();
        services.AddPublishObserver<WorkflowMessageObserver>();
        services.AddSendObserver<WorkflowMessageObserver>();
        services.AddWorkflowMessaging(configuration);        

        return services;
    }

    public static IServiceCollection AddWorkflowMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        var rabbitMqHost = configuration["RabbitMQ:Host"]
            ?? throw new InvalidOperationException("RabbitMQ:Host is not configured.");
        var rabbitMqUsername = configuration["RabbitMQ:Username"]
            ?? throw new InvalidOperationException("RabbitMQ:Username is not configured.");
        var rabbitMqPassword = configuration["RabbitMQ:Password"]
            ?? throw new InvalidOperationException("RabbitMQ:Password is not configured.");

        services.AddMassTransit(x =>
        {
            x.AddConsumer<ValidateApplicationConsumer>();
            x.AddConsumer<CheckPhotoConsumer>();
            x.AddConsumer<RiskAssessmentConsumer>();

            x.AddSagaStateMachine<ApplicationWorkflowStateMachine, ApplicationWorkflowState>()
                .EntityFrameworkRepository(repository =>
                {
                    repository.ConcurrencyMode = ConcurrencyMode.Pessimistic;
                    repository.ExistingDbContext<DrivingLicenceDbContext>();
                    repository.UsePostgres();
                });

            x.AddEntityFrameworkOutbox<DrivingLicenceDbContext>(options =>
            {
                options.UsePostgres();
                options.UseBusOutbox();
            });

            EndpointConvention.Map<ValidateApplication>(new Uri("queue:validate-application"));
            EndpointConvention.Map<CheckPhoto>(new Uri("queue:check-photo"));
            EndpointConvention.Map<AssessRisk>(new Uri("queue:assess-risk"));

            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(rabbitMqHost, "/", host =>
                {
                    host.Username(rabbitMqUsername);
                    host.Password(rabbitMqPassword);
                });

                cfg.ReceiveEndpoint("workflow-saga", endpoint =>
                {
                    ConfigureReliability(endpoint);
                    endpoint.UseEntityFrameworkOutbox<DrivingLicenceDbContext>(context);
                    endpoint.ConfigureSaga<ApplicationWorkflowState>(context);
                });

                cfg.ReceiveEndpoint("validate-application", endpoint =>
                {
                    ConfigureReliability(endpoint);
                    endpoint.UseEntityFrameworkOutbox<DrivingLicenceDbContext>(context);
                    endpoint.ConfigureConsumer<ValidateApplicationConsumer>(context);
                });

                cfg.ReceiveEndpoint("check-photo", endpoint =>
                {
                    ConfigureReliability(endpoint);
                    endpoint.UseEntityFrameworkOutbox<DrivingLicenceDbContext>(context);
                    endpoint.ConfigureConsumer<CheckPhotoConsumer>(context);
                });

                cfg.ReceiveEndpoint("assess-risk", endpoint =>
                {
                    ConfigureReliability(endpoint);
                    endpoint.UseEntityFrameworkOutbox<DrivingLicenceDbContext>(context);
                    endpoint.ConfigureConsumer<RiskAssessmentConsumer>(context);
                });
            });
        });

        return services;
    }

    private static void ConfigureReliability(IReceiveEndpointConfigurator endpoint)
    {
        endpoint.UseMessageRetry(retry =>
        {
            retry.Exponential(5, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5));
            retry.Ignore<WorkflowValidationException>();
        });
    }
}
