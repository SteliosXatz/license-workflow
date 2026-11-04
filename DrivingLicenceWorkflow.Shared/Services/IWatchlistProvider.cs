namespace DrivingLicenceWorkflow.Shared.Services;

public interface IWatchlistProvider
{
    bool Contains(string? nationalId);
}
