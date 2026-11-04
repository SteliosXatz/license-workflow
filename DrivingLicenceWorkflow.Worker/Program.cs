using DrivingLicenceWorkflow.Shared.Services;
using DrivingLicenceWorkflow.Worker.Extensions;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Json;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Services.AddSerilog((_, loggerConfiguration) =>
{
    loggerConfiguration
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
        .Enrich.FromLogContext()
        .WriteTo.File(
            new JsonFormatter(),
            builder.Configuration["Logging:FilePath"] ?? "logs/app-.log",
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 31);
});

builder.Services.AddWorkerServices(builder.Configuration);

var host = builder.Build();

// Fail at startup, not on the first risk assessment, if the watchlist file is missing.
host.Services.GetRequiredService<IWatchlistProvider>();

host.Run();
