using DrivingLicenceWorkflow.Shared.Services;
using DrivingLicenceWorkflow.Worker.Consumers;
using DrivingLicenceWorkflow.Worker.Services;
using MassTransit;
using DrivingLicenceWorkflow.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddBusinessRuleServices(builder.Configuration);
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<ValidateApplicationConsumer>();
    x.AddConsumer<CheckPhotoConsumer>();
    x.AddConsumer<RiskAssessmentConsumer>();
});
builder.Services.AddHostedService<Worker>();

var host = builder.Build();

// Fail at startup, not on the first risk assessment, if the watchlist file is missing.
host.Services.GetRequiredService<IWatchlistProvider>();

host.Run();
