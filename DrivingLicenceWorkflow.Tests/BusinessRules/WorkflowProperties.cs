using System.Security.Cryptography;
using DrivingLicenceWorkflow.Shared.Configuration;
using DrivingLicenceWorkflow.Shared.Domain;
using DrivingLicenceWorkflow.Shared.Services;
using FsCheck.Xunit;
using Microsoft.Extensions.Options;

namespace DrivingLicenceWorkflow.Tests.BusinessRules;

public sealed class WorkflowProperties
{
    private const int MinimumPhotoSize = 128;
    private const int MinimumPhotoScore = 50;

    [Property(MaxTest = 100)]
    // Feature: driving-licence-workflow, Property 1: Application ID uniqueness
    public void Generated_application_ids_are_unique_within_a_submission_sequence(int requestedCount)
    {
        var count = Math.Clamp(requestedCount & int.MaxValue, 1, 1000);
        var ids = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToArray();

        Assert.Equal(count, ids.Distinct().Count());
    }

    [Property(MaxTest = 100)]
    // Feature: driving-licence-workflow, Property 2: Data validation completeness
    public void Validation_reports_exactly_the_fields_that_fail(int rawMask, int ageFlag, bool validEmail)
    {
        var mask = rawMask & 0x1FF;
        var ageIsValid = (ageFlag & 1) != 0;
        string? Value(int bit, string value) => (mask & (1 << bit)) == 0 ? value : " ";

        var data = new ApplicationData(
            Value(0, "Jane"),
            Value(1, "Doe"),
            ageIsValid ? new DateTime(2008, 6, 15) : new DateTime(2009, 6, 15),
            Value(2, "NID-1"),
            (mask & (1 << 3)) != 0 ? " " : validEmail ? "jane@example.com" : "not-an-email",
            Value(4, "+30 210 0000000"),
            Value(5, "1 Main Street"),
            Value(6, "B"),
            Value(7, "cGhvdG8="));

        var result = new DataValidationService(
            Options.Create(new DataValidationOptions { MinApplicantAge = 18 }),
            TestData.Clock()).Validate(data);

        var expectedFields = new List<string>();
        var names = new[] { "firstName", "lastName", "nationalId", "email", "phoneNumber", "address", "licenceCategory", "photoBase64" };
        var bits = new[] { 0, 1, 2, 3, 4, 5, 6, 7 };
        for (var index = 0; index < names.Length; index++)
        {
            if ((mask & (1 << bits[index])) != 0)
            {
                expectedFields.Add(names[index]);
            }
        }

        if ((mask & (1 << 3)) == 0 && !validEmail)
        {
            expectedFields.Add("email");
        }

        if (!ageIsValid)
        {
            expectedFields.Add("dateOfBirth");
        }

        Assert.Equal(expectedFields.Count == 0, result.IsValid);
        Assert.Equal(expectedFields.Count, result.Errors.Count);
        Assert.Equal(expectedFields.Order(), result.Errors.Select(error => error.Split(' ', ':')[0]).Order());
    }

    [Property(MaxTest = 100)]
    // Feature: driving-licence-workflow, Property 3: Photo quality score determinism
    public void Photo_check_result_is_deterministic_for_any_input(string? photo)
    {
        var options = Options.Create(new PhotoCheckOptions
        {
            MinPhotoFileSizeBytes = 0,
            MinPhotoQualityScore = 0
        });

        var first = new PhotoCheckService(options).Check(photo);
        var second = new PhotoCheckService(options).Check(photo);

        Assert.Equal(first, second);
    }

    [Property(MaxTest = 100)]
    // Feature: driving-licence-workflow, Property 4: Photo check acceptance conditions
    public void Photo_is_accepted_if_and_only_if_every_configured_condition_holds(int rawInput)
    {
        var input = rawInput & 0xF;
        var isMissing = (input & 1) != 0;
        var isSupported = (input & 2) != 0;
        var isLargeEnough = (input & 4) != 0;
        var scoreMustFail = (input & 8) != 0;
        var bytes = new byte[isLargeEnough ? MinimumPhotoSize : MinimumPhotoSize - 1];
        (isSupported ? TestData.Photo.Jpeg : [0x47, 0x49, 0x46, 0x38]).CopyTo(bytes, 0);
        BitConverter.GetBytes(input).CopyTo(bytes, 8);

        var score = SHA256.HashData(bytes)[0] % 100;
        var minimumScore = scoreMustFail ? score + 1 : score;
        var photo = isMissing ? null : Convert.ToBase64String(bytes);
        var result = new PhotoCheckService(Options.Create(new PhotoCheckOptions
        {
            MinPhotoFileSizeBytes = MinimumPhotoSize,
            MinPhotoQualityScore = minimumScore
        })).Check(photo);
        var expectedAccepted = !isMissing && isSupported && isLargeEnough && !scoreMustFail;

        Assert.Equal(expectedAccepted, result.IsAcceptable);
        if (!expectedAccepted)
        {
            Assert.False(string.IsNullOrWhiteSpace(result.FailureReason));
        }
    }

    [Property(MaxTest = 100)]
    // Feature: driving-licence-workflow, Property 5: Risk assessment decision
    public void Risk_review_is_required_if_and_only_if_any_risk_condition_holds(int rawConditions)
    {
        var conditions = rawConditions & 0xF;
        var belowAgeThreshold = (conditions & 1) != 0;
        var nonStandardCategory = (conditions & 2) != 0;
        var watchedNationalId = (conditions & 4) != 0;
        var belowPhotoThreshold = (conditions & 8) != 0;
        var service = new RiskAssessmentService(
            Options.Create(new RiskAssessmentOptions
            {
                RiskAgeThreshold = 21,
                PhotoQualityThreshold = MinimumPhotoScore
            }),
            new PropertyWatchlist(watchedNationalId ? "WATCHED" : "OTHER"),
            TestData.Clock());

        var result = service.Assess(new RiskAssessmentInput(
            belowAgeThreshold ? new DateTime(2006, 6, 15) : new DateTime(2005, 6, 15),
            nonStandardCategory ? "A" : "B",
            watchedNationalId ? "WATCHED" : "CLEAR",
            belowPhotoThreshold ? MinimumPhotoScore - 1 : MinimumPhotoScore));

        Assert.Equal(conditions != 0, result.RequiresManualReview);
        if (belowAgeThreshold)
        {
            Assert.Contains("age", result.Reason);
        }

        if (nonStandardCategory)
        {
            Assert.Contains("category", result.Reason);
        }

        if (watchedNationalId)
        {
            Assert.Contains("watchlist", result.Reason);
        }

        if (belowPhotoThreshold)
        {
            Assert.Contains("score", result.Reason);
        }
    }

    private sealed class PropertyWatchlist(string watchedId) : IWatchlistProvider
    {
        public bool Contains(string? nationalId) =>
            string.Equals(nationalId, watchedId, StringComparison.OrdinalIgnoreCase);
    }
}
