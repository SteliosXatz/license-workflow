using DrivingLicenceWorkflow.Api.Contracts;
using DrivingLicenceWorkflow.Api.Validators;
using DrivingLicenceWorkflow.Shared.Data;
using DrivingLicenceWorkflow.Shared.Services;
using FluentValidation;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var appDbConnectionString = builder.Configuration.GetConnectionString("AppDb")
    ?? throw new InvalidOperationException("ConnectionStrings:AppDb is not configured.");
builder.Services.AddDbContext<DrivingLicenceDbContext>(options =>
    options.UseNpgsql(appDbConnectionString));

builder.Services.AddDataValidationService(builder.Configuration);
builder.Services.AddScoped<IValidator<SubmitApplicationRequest>, SubmitApplicationRequestValidator>();
builder.Services.AddScoped<IValidator<ManualReviewActionRequest>, ManualReviewActionRequestValidator>();

var rabbitMqHost = builder.Configuration["RabbitMQ:Host"]
    ?? throw new InvalidOperationException("RabbitMQ:Host is not configured.");
var rabbitMqUsername = builder.Configuration["RabbitMQ:Username"]
    ?? throw new InvalidOperationException("RabbitMQ:Username is not configured.");
var rabbitMqPassword = builder.Configuration["RabbitMQ:Password"]
    ?? throw new InvalidOperationException("RabbitMQ:Password is not configured.");

builder.Services.AddMassTransit(x =>
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

var app = builder.Build();

// Migrates in place on startup; see "Migrations" in README.md for trade-offs.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DrivingLicenceDbContext>();
    await db.Database.MigrateAsync();
}

// Configure the HTTP request pipeline.
app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();
app.MapControllers();

app.Run();
