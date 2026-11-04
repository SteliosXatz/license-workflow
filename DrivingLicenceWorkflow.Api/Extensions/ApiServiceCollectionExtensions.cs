using System.Text.Json.Serialization;
using DrivingLicenceWorkflow.Api.Contracts;
using DrivingLicenceWorkflow.Api.Validators;
using DrivingLicenceWorkflow.Shared.Data;
using DrivingLicenceWorkflow.Shared.Services;
using FluentValidation;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace DrivingLicenceWorkflow.Api.Extensions;

public static class ApiServiceCollectionExtensions
{
    public static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();
        services
            .AddControllers()
            .AddJsonOptions(options =>
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        var appDbConnectionString = configuration.GetConnectionString("AppDb")
            ?? throw new InvalidOperationException("ConnectionStrings:AppDb is not configured.");
        services.AddDbContext<DrivingLicenceDbContext>(options =>
            options.UseNpgsql(appDbConnectionString));

        services.AddDataValidationService(configuration);
        services.AddScoped<IValidator<SubmitApplicationRequest>, SubmitApplicationRequestValidator>();
        services.AddScoped<IValidator<ManualReviewActionRequest>, ManualReviewActionRequestValidator>();
        services.AddApiMessaging(configuration);

        return services;
    }

    public static IServiceCollection AddApiMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        var rabbitMqHost = configuration["RabbitMQ:Host"]
            ?? throw new InvalidOperationException("RabbitMQ:Host is not configured.");
        var rabbitMqUsername = configuration["RabbitMQ:Username"]
            ?? throw new InvalidOperationException("RabbitMQ:Username is not configured.");
        var rabbitMqPassword = configuration["RabbitMQ:Password"]
            ?? throw new InvalidOperationException("RabbitMQ:Password is not configured.");

        services.AddMassTransit(x =>
        {
            x.AddEntityFrameworkOutbox<DrivingLicenceDbContext>(options =>
            {
                options.UsePostgres();
                options.UseBusOutbox();
            });

            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(rabbitMqHost, "/", host =>
                {
                    host.Username(rabbitMqUsername);
                    host.Password(rabbitMqPassword);
                });
                cfg.ConfigureEndpoints(context);
            });
        });

        return services;
    }
}
