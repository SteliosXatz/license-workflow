using DrivingLicenceWorkflow.Shared.Configuration;
using Microsoft.Extensions.Options;

namespace DrivingLicenceWorkflow.Shared.Services;

// Loads the file once at construction, so a missing file fails fast instead of silently approving everyone.
public class FileWatchlistProvider : IWatchlistProvider
{
    private readonly HashSet<string> _nationalIds;

    public FileWatchlistProvider(IOptions<RiskAssessmentOptions> options)
    {
        var path = Resolve(options.Value.WatchlistPath);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Watchlist file not found: {path}", path);
        }

        _nationalIds = File.ReadLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public bool Contains(string? nationalId) =>
        !string.IsNullOrWhiteSpace(nationalId) && _nationalIds.Contains(nationalId.Trim());

    // Relative paths resolve against the application directory, not the working directory.
    private static string Resolve(string path) =>
        Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
}
