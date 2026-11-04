using DrivingLicenceWorkflow.Shared.Configuration;
using DrivingLicenceWorkflow.Shared.Services;
using Microsoft.Extensions.Options;

namespace DrivingLicenceWorkflow.Tests.BusinessRules;

public sealed class FileWatchlistProviderTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("watchlist-tests").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string WriteFile(string content, string name = "watchlist.txt")
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static FileWatchlistProvider Create(string path) =>
        new(Options.Create(new RiskAssessmentOptions { WatchlistPath = path }));

    [Fact]
    public void Listed_ids_are_found_and_others_are_not()
    {
        var provider = Create(WriteFile("AB123456\nXY789012\n"));

        Assert.True(provider.Contains("AB123456"));
        Assert.True(provider.Contains("XY789012"));
        Assert.False(provider.Contains("ZZ000000"));
    }

    [Fact]
    public void Matching_ignores_case_and_surrounding_whitespace()
    {
        var provider = Create(WriteFile("  ab123456  \n"));

        Assert.True(provider.Contains("AB123456"));
        Assert.True(provider.Contains("  Ab123456 "));
    }

    [Fact]
    public void Blank_lines_and_comments_are_ignored()
    {
        var provider = Create(WriteFile("# header\n\n   \nAB123456\n# trailing comment\n"));

        Assert.True(provider.Contains("AB123456"));
        Assert.False(provider.Contains("# header"));
        Assert.False(provider.Contains(""));
        Assert.False(provider.Contains("trailing comment"));
    }

    [Fact]
    public void Windows_line_endings_are_handled()
    {
        var provider = Create(WriteFile("AB123456\r\nXY789012\r\n"));

        Assert.True(provider.Contains("AB123456"));
        Assert.True(provider.Contains("XY789012"));
    }

    [Fact]
    public void Duplicate_entries_are_harmless()
    {
        var provider = Create(WriteFile("AB123456\nab123456\n"));

        Assert.True(provider.Contains("AB123456"));
    }

    [Fact]
    public void Empty_file_is_a_valid_empty_watchlist()
    {
        var provider = Create(WriteFile(""));

        Assert.False(provider.Contains("AB123456"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Null_or_blank_ids_are_never_on_the_watchlist(string? nationalId)
    {
        var provider = Create(WriteFile("AB123456\n"));

        Assert.False(provider.Contains(nationalId));
    }

    [Fact]
    public void Missing_file_fails_fast_with_the_resolved_path()
    {
        var path = Path.Combine(_directory, "missing.txt");

        var error = Assert.Throws<FileNotFoundException>(() => Create(path));

        Assert.Contains(path, error.Message);
    }

    [Fact]
    public void Relative_path_is_resolved_against_the_application_directory()
    {
        var name = $"watchlist-{Guid.NewGuid():N}.txt";
        var path = Path.Combine(AppContext.BaseDirectory, name);
        File.WriteAllText(path, "REL-1\n");
        try
        {
            Assert.True(Create(name).Contains("REL-1"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
