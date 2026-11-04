namespace DrivingLicenceWorkflow.Shared.Configuration;

public class DataValidationOptions
{
    public const string SectionName = "DataValidation";

    public int MinApplicantAge { get; set; } = 18;
}
