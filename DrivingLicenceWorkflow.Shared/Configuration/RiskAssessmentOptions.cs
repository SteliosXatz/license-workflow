namespace DrivingLicenceWorkflow.Shared.Configuration;

public class RiskAssessmentOptions
{
    public const string SectionName = "RiskAssessment";

    public string WatchlistPath { get; set; } = "watchlist.txt";
    public int PhotoQualityThreshold { get; set; } = 50;
    public int RiskAgeThreshold { get; set; } = 21;
}
