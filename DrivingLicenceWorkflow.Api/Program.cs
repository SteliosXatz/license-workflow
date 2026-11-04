using DrivingLicenceWorkflow.Api.Extensions;
using DrivingLicenceWorkflow.Shared.Data;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Services.AddSerilog((_, loggerConfiguration) =>
{
    loggerConfiguration
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.AspNetCore.HttpLogging", LogEventLevel.Information)
        .Enrich.FromLogContext()
        .WriteTo.File(
            new JsonFormatter(),
            builder.Configuration["Logging:FilePath"] ?? "logs/app-.log",
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 31);
});

builder.Services.AddApiServices(builder.Configuration);
builder.Services.AddApiObservability(builder.Configuration, builder.Environment);

var app = builder.Build();

// Migrates in place on startup; see "Migrations" in README.md for trade-offs.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DrivingLicenceDbContext>();
    await db.Database.MigrateAsync();
}

app.UseSerilogRequestLogging();
app.UseHttpLogging();
app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();
app.MapControllers();
app.MapPrometheusScrapingEndpoint();

app.Run();
