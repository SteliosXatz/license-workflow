using DrivingLicenceWorkflow.Shared.Configuration;
using DrivingLicenceWorkflow.Shared.Services;
using Microsoft.Extensions.Options;

namespace DrivingLicenceWorkflow.Tests.BusinessRules;

public class PhotoCheckServiceTests
{
    private const int MinSize = 1024;

    private static PhotoCheckService Create(int minSize = MinSize, int minScore = 20) =>
        new(Options.Create(new PhotoCheckOptions { MinPhotoFileSizeBytes = minSize, MinPhotoQualityScore = minScore }));

    private static string AcceptablePhoto(byte[] signature, int size = MinSize, int minScore = 20) =>
        TestData.Photo.ToBase64(TestData.Photo.Create(signature, size, s => s >= minScore));

    [Theory]
    [InlineData("jpeg")]
    [InlineData("png")]
    public void Supported_photo_types_are_accepted(string kind)
    {
        var signature = kind == "jpeg" ? TestData.Photo.Jpeg : TestData.Photo.Png;

        var result = Create().Check(AcceptablePhoto(signature));

        Assert.True(result.IsAcceptable);
        Assert.Null(result.FailureReason);
    }

    [Fact]
    public void Data_uri_prefix_is_accepted()
    {
        var photo = "data:image/jpeg;base64," + AcceptablePhoto(TestData.Photo.Jpeg);

        Assert.True(Create().Check(photo).IsAcceptable);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_photo_is_rejected(string? photo)
    {
        var result = Create().Check(photo);

        Assert.False(result.IsAcceptable);
        Assert.Equal(0, result.QualityScore);
        Assert.Contains("No photo", result.FailureReason);
    }

    [Theory]
    [InlineData("not base64 !!!")]
    [InlineData("abc")]
    public void Invalid_base64_is_rejected(string photo)
    {
        var result = Create().Check(photo);

        Assert.False(result.IsAcceptable);
        Assert.Contains("base64", result.FailureReason);
    }

    public static IEnumerable<object[]> UnsupportedHeaders()
    {
        yield return new object[] { "gif", new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 } };
        yield return new object[] { "bmp", new byte[] { 0x42, 0x4D } };
        yield return new object[] { "pdf", new byte[] { 0x25, 0x50, 0x44, 0x46 } };
        yield return new object[] { "truncated jpeg signature", new byte[] { 0xFF, 0xD8 } };
        yield return new object[] { "truncated png signature", new byte[] { 0x89, 0x50, 0x4E, 0x47 } };
        yield return new object[] { "all zeros", new byte[8] };
    }

    [Theory]
    [MemberData(nameof(UnsupportedHeaders))]
    public void Unsupported_file_type_is_rejected(string description, byte[] signature)
    {
        _ = description;
        var bytes = TestData.Photo.Create(signature, MinSize);

        var result = Create().Check(TestData.Photo.ToBase64(bytes));

        Assert.False(result.IsAcceptable);
        Assert.Contains("Unsupported", result.FailureReason);
    }

    [Fact]
    public void File_exactly_at_the_minimum_size_is_accepted()
    {
        var result = Create().Check(AcceptablePhoto(TestData.Photo.Jpeg, size: MinSize));

        Assert.True(result.IsAcceptable);
    }

    [Fact]
    public void File_one_byte_under_the_minimum_size_is_rejected_with_its_score_reported()
    {
        var bytes = TestData.Photo.Create(TestData.Photo.Jpeg, MinSize - 1, s => s >= 20);

        var result = Create().Check(TestData.Photo.ToBase64(bytes));

        Assert.False(result.IsAcceptable);
        Assert.Contains("file size", result.FailureReason);
        Assert.Equal(TestData.Photo.ExpectedScore(bytes), result.QualityScore);
    }

    [Fact]
    public void Minimum_file_size_is_configurable()
    {
        var photo = AcceptablePhoto(TestData.Photo.Png, size: 2000);

        Assert.True(Create(minSize: 2000).Check(photo).IsAcceptable);
        Assert.False(Create(minSize: 2001).Check(photo).IsAcceptable);
    }

    [Fact]
    public void Score_exactly_at_the_minimum_is_accepted_and_one_below_is_rejected()
    {
        var bytes = TestData.Photo.Create(TestData.Photo.Jpeg, MinSize);
        var photo = TestData.Photo.ToBase64(bytes);
        var score = TestData.Photo.ExpectedScore(bytes);

        Assert.True(Create(minScore: score).Check(photo).IsAcceptable);

        var rejected = Create(minScore: score + 1).Check(photo);
        Assert.False(rejected.IsAcceptable);
        Assert.Equal(score, rejected.QualityScore);
        Assert.Contains("quality score", rejected.FailureReason);
    }

    [Fact]
    public void Score_is_the_first_hash_byte_modulo_100_and_is_reproducible()
    {
        var bytes = TestData.Photo.Create(TestData.Photo.Png, MinSize);
        var photo = TestData.Photo.ToBase64(bytes);
        var service = Create(minScore: 0);

        var first = service.Check(photo);
        var second = Create(minScore: 0).Check(photo);

        Assert.Equal(TestData.Photo.ExpectedScore(bytes), first.QualityScore);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Score_stays_within_0_to_99()
    {
        var service = Create(minScore: 0, minSize: 0);

        for (var seed = 0; seed < 500; seed++)
        {
            var bytes = TestData.Photo.Jpeg.Concat(BitConverter.GetBytes(seed)).ToArray();
            var score = service.Check(TestData.Photo.ToBase64(bytes)).QualityScore;
            Assert.InRange(score, 0, 99);
        }
    }

    [Fact]
    public void File_type_is_reported_before_file_size()
    {
        var tiny = TestData.Photo.ToBase64(new byte[] { 0x47, 0x49, 0x46 });

        var result = Create().Check(tiny);

        Assert.Contains("Unsupported", result.FailureReason);
    }
}
