using DrivingLicenceWorkflow.Shared.Configuration;
using DrivingLicenceWorkflow.Shared.Services;
using Microsoft.Extensions.Options;

namespace DrivingLicenceWorkflow.Tests.BusinessRules;

public class DataValidationServiceTests
{
    private static DataValidationService Create(int minAge = 18, DateTimeOffset? now = null) =>
        new(Options.Create(new DataValidationOptions { MinApplicantAge = minAge }), TestData.Clock(now));

    [Fact]
    public void Valid_application_passes_with_no_errors()
    {
        var result = Create().Validate(TestData.ValidApplication());

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Applicant_who_turns_18_today_passes()
    {
        var result = Create().Validate(TestData.ValidApplication(dateOfBirth: new DateTime(2008, 6, 15)));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Applicant_one_day_short_of_18_fails_with_a_dateOfBirth_error()
    {
        var result = Create().Validate(TestData.ValidApplication(dateOfBirth: new DateTime(2008, 6, 16)));

        Assert.False(result.IsValid);
        Assert.StartsWith("dateOfBirth", Assert.Single(result.Errors));
    }

    [Fact]
    public void Age_is_measured_on_the_calendar_date_not_the_time_of_day()
    {
        var now = new DateTimeOffset(2026, 6, 15, 0, 1, 0, TimeSpan.Zero);

        // Born late in the evening on the same calendar date: still turns 18 today.
        var result = Create(now: now)
            .Validate(TestData.ValidApplication(dateOfBirth: new DateTime(2008, 6, 15, 23, 59, 0)));

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(2026, 2, 28, false)]
    [InlineData(2026, 3, 1, true)]
    public void Leap_day_birthday_turns_18_on_1_March_in_a_non_leap_year(int year, int month, int day, bool expectedValid)
    {
        var now = new DateTimeOffset(year, month, day, 12, 0, 0, TimeSpan.Zero);

        var result = Create(now: now).Validate(TestData.ValidApplication(dateOfBirth: new DateTime(2008, 2, 29)));

        Assert.Equal(expectedValid, result.IsValid);
    }

    [Fact]
    public void Date_of_birth_in_the_future_fails()
    {
        var result = Create().Validate(TestData.ValidApplication(dateOfBirth: new DateTime(2030, 1, 1)));

        Assert.False(result.IsValid);
        Assert.StartsWith("dateOfBirth", Assert.Single(result.Errors));
    }

    [Fact]
    public void Missing_date_of_birth_reports_required_and_not_an_age_error()
    {
        var data = TestData.ValidApplication() with { DateOfBirth = default };

        var result = Create().Validate(data);

        Assert.False(result.IsValid);
        Assert.Equal(new[] { "dateOfBirth is required." }, result.Errors);
    }

    [Fact]
    public void Minimum_age_is_configurable()
    {
        var applicant = TestData.ValidApplication(dateOfBirth: new DateTime(2008, 6, 15)); // exactly 18

        Assert.True(Create(minAge: 18).Validate(applicant).IsValid);
        Assert.False(Create(minAge: 21).Validate(applicant).IsValid);
    }

    public static IEnumerable<object?[]> MissingFieldCases()
    {
        foreach (var value in new string?[] { null, "", "   " })
        {
            yield return new object?[] { "firstName", TestData.ValidApplication(firstName: value) };
            yield return new object?[] { "lastName", TestData.ValidApplication(lastName: value) };
            yield return new object?[] { "nationalId", TestData.ValidApplication(nationalId: value) };
            yield return new object?[] { "email", TestData.ValidApplication(email: value) };
            yield return new object?[] { "phoneNumber", TestData.ValidApplication(phoneNumber: value) };
            yield return new object?[] { "address", TestData.ValidApplication(address: value) };
            yield return new object?[] { "licenceCategory", TestData.ValidApplication(licenceCategory: value) };
            yield return new object?[] { "photoBase64", TestData.ValidApplication(photoBase64: value) };
        }
    }

    [Theory]
    [MemberData(nameof(MissingFieldCases))]
    public void Each_missing_field_is_reported_alone_by_name(string field, ApplicationData data)
    {
        var result = Create().Validate(data);

        Assert.False(result.IsValid);
        Assert.Equal($"{field} is required.", Assert.Single(result.Errors));
    }

    [Fact]
    public void All_problems_are_reported_together()
    {
        var data = new ApplicationData(null, null, default, null, null, null, null, null, null);

        var result = Create().Validate(data);

        Assert.False(result.IsValid);
        Assert.Equal(9, result.Errors.Count);
        Assert.Equal(result.Errors.Count, result.Errors.Distinct().Count());
    }

    [Theory]
    [InlineData("jane@example.com")]
    [InlineData("first.last+tag@sub.example.org")]
    [InlineData("UPPER@EXAMPLE.COM")]
    public void Valid_emails_pass(string email)
    {
        Assert.True(Create().Validate(TestData.ValidApplication(email: email)).IsValid);
    }

    [Theory]
    [InlineData("plainaddress")]
    [InlineData("missing-domain@")]
    [InlineData("@missing-local.com")]
    [InlineData("no-dot@localhost")]
    [InlineData("two@@example.com")]
    [InlineData("spaces in@example.com")]
    [InlineData("Jane Doe <jane@example.com>")]
    public void Invalid_emails_fail_with_an_email_error(string email)
    {
        var result = Create().Validate(TestData.ValidApplication(email: email));

        Assert.False(result.IsValid);
        Assert.Equal("email is not a valid email address.", Assert.Single(result.Errors));
    }
}
