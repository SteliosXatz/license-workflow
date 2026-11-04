namespace DrivingLicenceWorkflow.Shared.Configuration;

public class PhotoCheckOptions
{
    public const string SectionName = "PhotoCheck";

    public int MinPhotoFileSizeBytes { get; set; } = 1024;
    public int MinPhotoQualityScore { get; set; } = 20;
}
