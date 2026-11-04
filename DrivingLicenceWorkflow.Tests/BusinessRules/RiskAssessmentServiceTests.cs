using DrivingLicenceWorkflow.Shared.Configuration;
using DrivingLicenceWorkflow.Shared.Services;
using Microsoft.Extensions.Options;

namespace DrivingLicenceWorkflow.Tests.BusinessRules;

public class RiskAssessmentServiceTests
{
    private const string WatchedId = "WATCHED-1";

    private sealed class FakeWatchlist(params string[] ids) : IWatchlistProvider
    {
        private readonly HashSet<string> _ids = new(ids, StringComparer.OrdinalIgnoreCase);

        public bool Contains(string? nationalId) => nationalId is not null && _ids.Contains(nationalId);
    }

    private static RiskAssessmentService Create(int ageThreshold = 21, int scoreThreshold = 50) =>
        new(
            Options.Create(new RiskAssessmentOptions
            {
                RiskAgeThreshold = ageThreshold,
                PhotoQualityThreshold = scoreThreshold
            }),
            new FakeWatchlist(WatchedId),
            TestData.Clock());

    // Born 1990, category B, not watched, score 80: triggers nothing by default.
    private static RiskAssessmentInput Input(
        DateTime? dob = null, string category = "B", string nationalId = "NID-1", int score = 80) =>
        new(dob ?? new DateTime(1990, 1, 1), category, nationalId, score);

    [Fact]
    public void No_risk_conditions_means_automatic_approval()
    {
        var result = Create().Assess(Input());

        Assert.False(result.RequiresManualReview);
        Assert.Null(result.Reason);
    }

    [Fact]
    public void Applicant_under_21_requires_manual_review()
    {
        var result = Create().Assess(Input(dob: new DateTime(2006, 6, 16))); // 19

        Assert.True(result.RequiresManualReview);
        Assert.Contains("age 19", result.Reason);
    }

    [Fact]
    public void Licence_category_other_than_B_requires_manual_review()
    {
        var result = Create().Assess(Input(category: "A"));

        Assert.True(result.RequiresManualReview);
        Assert.Contains("category 'A'", result.Reason);
    }

    [Fact]
    public void National_id_on_the_watchlist_requires_manual_review()
    {
        var result = Create().Assess(Input(nationalId: WatchedId));

        Assert.True(result.RequiresManualReview);
        Assert.Contains("watchlist", result.Reason);
    }

    [Fact]
    public void Photo_score_below_the_threshold_requires_manual_review()
    {
        var result = Create().Assess(Input(score: 49));

        Assert.True(result.RequiresManualReview);
        Assert.Contains("score 49", result.Reason);
    }

    [Fact]
    public void Turning_21_today_is_not_a_risk_but_one_day_earlier_is()
    {
        var service = Create();

        Assert.False(service.Assess(Input(dob: new DateTime(2005, 6, 15))).RequiresManualReview);
        Assert.True(service.Assess(Input(dob: new DateTime(2005, 6, 16))).RequiresManualReview);
    }

    [Fact]
    public void Score_exactly_at_the_threshold_is_not_a_risk()
    {
        var service = Create(scoreThreshold: 50);

        Assert.False(service.Assess(Input(score: 50)).RequiresManualReview);
        Assert.True(service.Assess(Input(score: 49)).RequiresManualReview);
    }

    [Theory]
    [InlineData("B")]
    [InlineData("b")]
    [InlineData(" B ")]
    public void Category_B_is_matched_ignoring_case_and_surrounding_spaces(string category)
    {
        Assert.False(Create().Assess(Input(category: category)).RequiresManualReview);
    }

    [Theory]
    [InlineData("")]
    [InlineData("BE")]
    [InlineData("B1")]
    [InlineData("C")]
    public void Any_other_category_requires_manual_review(string category)
    {
        Assert.True(Create().Assess(Input(category: category)).RequiresManualReview);
    }

    [Fact]
    public void National_id_match_is_decided_by_the_watchlist_provider()
    {
        Assert.True(Create().Assess(Input(nationalId: WatchedId.ToLowerInvariant())).RequiresManualReview);
    }

    [Fact]
    public void Every_triggered_condition_is_listed_in_the_reason()
    {
        var result = Create().Assess(new RiskAssessmentInput(new DateTime(2010, 1, 1), "A", WatchedId, 10));

        Assert.True(result.RequiresManualReview);
        Assert.Contains("age", result.Reason);
        Assert.Contains("category", result.Reason);
        Assert.Contains("watchlist", result.Reason);
        Assert.Contains("score", result.Reason);
    }

    [Fact]
    public void Only_the_triggered_conditions_appear_in_the_reason()
    {
        var result = Create().Assess(Input(category: "A"));

        Assert.DoesNotContain("watchlist", result.Reason);
        Assert.DoesNotContain("score", result.Reason);
        Assert.DoesNotContain("age", result.Reason);
    }

    [Fact]
    public void Age_and_score_thresholds_are_configurable()
    {
        var applicant = Input(dob: new DateTime(2000, 1, 1), score: 70); // 26 years old, score 70

        Assert.False(Create().Assess(applicant).RequiresManualReview);
        Assert.True(Create(ageThreshold: 30).Assess(applicant).RequiresManualReview);
        Assert.True(Create(scoreThreshold: 71).Assess(applicant).RequiresManualReview);
    }
}
