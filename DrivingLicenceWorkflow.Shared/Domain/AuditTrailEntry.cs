namespace DrivingLicenceWorkflow.Shared.Domain;

public class AuditTrailEntry
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public DateTime Timestamp { get; set; }
    public ApplicationStatus? FromStatus { get; set; }
    public ApplicationStatus ToStatus { get; set; }
    public string Event { get; set; } = string.Empty;
    public string? Details { get; set; }
}
