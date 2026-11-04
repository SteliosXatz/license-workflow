using DrivingLicenceWorkflow.Shared.Services;
using DrivingLicenceWorkflow.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddBusinessRuleServices(builder.Configuration);
builder.Services.AddHostedService<Worker>();

var host = builder.Build();

// Fail at startup, not on the first risk assessment, if the watchlist file is missing.
host.Services.GetRequiredService<IWatchlistProvider>();

host.Run();
