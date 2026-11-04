using DrivingLicenceWorkflow.Shared.Configuration;
using Microsoft.Extensions.Options;

namespace DrivingLicenceWorkflow.Shared.Services;

public class RiskAssessmentService : IRiskAssessmentService
{
    private const string StandardLicenceCategory = "B";

    private readonly RiskAssessmentOptions _options;
    private readonly IWatchlistProvider _watchlist;
    private readonly TimeProvider _timeProvider;

    public RiskAssessmentService(
        IOptions<RiskAssessmentOptions> options, IWatchlistProvider watchlist, TimeProvider timeProvider)
    {
        _options = options.Value;
        _watchlist = watchlist;
        _timeProvider = timeProvider;
    }

    public RiskAssessmentResult Assess(RiskAssessmentInput input)
    {
        var reasons = new List<string>();

        var age = AgeCalculator.AgeOn(input.DateOfBirth, _timeProvider.GetUtcNow().UtcDateTime);
        if (age < _options.RiskAgeThreshold)
        {
            reasons.Add($"Applicant age {age} is below {_options.RiskAgeThreshold}.");
        }

        if (!string.Equals(input.LicenceCategory?.Trim(), StandardLicenceCategory, StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add($"Licence category '{input.LicenceCategory}' is not {StandardLicenceCategory}.");
        }

        if (_watchlist.Contains(input.NationalId))
        {
            reasons.Add("National ID is on the watchlist.");
        }

        if (input.PhotoQualityScore < _options.PhotoQualityThreshold)
        {
            reasons.Add($"Photo quality score {input.PhotoQualityScore} is below {_options.PhotoQualityThreshold}.");
        }

        return reasons.Count == 0
            ? new RiskAssessmentResult(false, null)
            : new RiskAssessmentResult(true, string.Join(" ", reasons));
    }
}
