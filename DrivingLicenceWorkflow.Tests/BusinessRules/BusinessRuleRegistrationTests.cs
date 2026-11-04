using DrivingLicenceWorkflow.Shared.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DrivingLicenceWorkflow.Tests.BusinessRules;

public sealed class BusinessRuleRegistrationTests : IDisposable
{
    private readonly string _watchlist = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.txt");

    public BusinessRuleRegistrationTests() => File.WriteAllText(_watchlist, "AB123456\n");

    public void Dispose() => File.Delete(_watchlist);

    private IConfiguration Configuration(int minAge = 18) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataValidation:MinApplicantAge"] = minAge.ToString(),
                ["PhotoCheck:MinPhotoFileSizeBytes"] = "10",
                ["PhotoCheck:MinPhotoQualityScore"] = "0",
                ["RiskAssessment:WatchlistPath"] = _watchlist,
                ["RiskAssessment:PhotoQualityThreshold"] = "50",
                ["RiskAssessment:RiskAgeThreshold"] = "21"
            })
            .Build();

    [Fact]
    public void Api_registration_contains_data_validation_only()
    {
        using var provider = new ServiceCollection()
            .AddDataValidationService(Configuration())
            .BuildServiceProvider();

        Assert.NotNull(provider.GetService<IDataValidationService>());
        Assert.Null(provider.GetService<IPhotoCheckService>());
        Assert.Null(provider.GetService<IRiskAssessmentService>());
        Assert.Null(provider.GetService<IWatchlistProvider>());
    }

    [Fact]
    public void Worker_registration_resolves_every_service()
    {
        using var provider = new ServiceCollection()
            .AddBusinessRuleServices(Configuration())
            .BuildServiceProvider();

        Assert.IsType<DataValidationService>(provider.GetRequiredService<IDataValidationService>());
        Assert.IsType<PhotoCheckService>(provider.GetRequiredService<IPhotoCheckService>());
        Assert.IsType<RiskAssessmentService>(provider.GetRequiredService<IRiskAssessmentService>());
        Assert.IsType<FileWatchlistProvider>(provider.GetRequiredService<IWatchlistProvider>());
    }

    [Fact]
    public void Options_are_bound_from_the_configuration_sections()
    {
        using var provider = new ServiceCollection()
            .AddBusinessRuleServices(Configuration(minAge: 25))
            .BuildServiceProvider();

        var validation = provider.GetRequiredService<IDataValidationService>();
        var applicant = TestData.ValidApplication(dateOfBirth: DateTime.UtcNow.AddYears(-20));

        Assert.False(validation.Validate(applicant).IsValid);
        Assert.True(provider.GetRequiredService<IWatchlistProvider>().Contains("AB123456"));
    }

    [Fact]
    public void Registering_all_services_keeps_a_single_time_provider()
    {
        using var provider = new ServiceCollection()
            .AddBusinessRuleServices(Configuration())
            .BuildServiceProvider();

        Assert.Same(TimeProvider.System, provider.GetRequiredService<TimeProvider>());
        Assert.Single(new ServiceCollection().AddBusinessRuleServices(Configuration()), d => d.ServiceType == typeof(TimeProvider));
    }
}
