using DrivingLicenceWorkflow.Shared.Services;
using DrivingLicenceWorkflow.Worker.Extensions;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWorkerServices(builder.Configuration);

var host = builder.Build();

// Fail at startup, not on the first risk assessment, if the watchlist file is missing.
host.Services.GetRequiredService<IWatchlistProvider>();

host.Run();
