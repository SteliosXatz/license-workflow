namespace DrivingLicenceWorkflow.Shared.Domain;

public class Application
{
    public Guid Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public DateTime DateOfBirth { get; set; }
    public string NationalId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string LicenceCategory { get; set; } = string.Empty;
    public string PhotoBase64 { get; set; } = string.Empty;
    public ApplicationStatus Status { get; set; } = ApplicationStatus.Submitted;
    public string? RejectionReason { get; set; }
    public int? PhotoQualityScore { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<AuditTrailEntry> AuditTrail { get; set; } = new();
}
