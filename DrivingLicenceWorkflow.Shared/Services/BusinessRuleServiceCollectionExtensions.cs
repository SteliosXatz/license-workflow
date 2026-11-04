using DrivingLicenceWorkflow.Shared.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DrivingLicenceWorkflow.Shared.Services;

public static class BusinessRuleServiceCollectionExtensions
{
    public static IServiceCollection AddDataValidationService(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DataValidationOptions>(configuration.GetSection(DataValidationOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IDataValidationService, DataValidationService>();
        return services;
    }

    public static IServiceCollection AddPhotoCheckService(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PhotoCheckOptions>(configuration.GetSection(PhotoCheckOptions.SectionName));
        services.AddSingleton<IPhotoCheckService, PhotoCheckService>();
        return services;
    }

    public static IServiceCollection AddRiskAssessmentService(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RiskAssessmentOptions>(configuration.GetSection(RiskAssessmentOptions.SectionName));
        services.AddSingleton<IWatchlistProvider, FileWatchlistProvider>();
        services.AddSingleton<IRiskAssessmentService, RiskAssessmentService>();
        return services;
    }

    public static IServiceCollection AddBusinessRuleServices(this IServiceCollection services, IConfiguration configuration) =>
        services
            .AddDataValidationService(configuration)
            .AddPhotoCheckService(configuration)
            .AddRiskAssessmentService(configuration);
}
