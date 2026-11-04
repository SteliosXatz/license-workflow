using DrivingLicenceWorkflow.Shared.Domain;

namespace DrivingLicenceWorkflow.Api.Contracts;

public sealed record SubmitApplicationRequest
{
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public DateTime DateOfBirth { get; init; }
    public string? NationalId { get; init; }
    public string? Email { get; init; }
    public string? PhoneNumber { get; init; }
    public string? Address { get; init; }
    public string? LicenceCategory { get; init; }
    public string? PhotoBase64 { get; init; }
}

public sealed record SubmitApplicationResponse(Guid ApplicationId, ApplicationStatus Status);

public sealed record ApplicationStatusResponse(Guid Id, ApplicationStatus Status, DateTime CreatedAt, DateTime UpdatedAt);

public sealed record AuditTrailEntryResponse(
    DateTime Timestamp,
    ApplicationStatus? FromStatus,
    ApplicationStatus ToStatus,
    string Event,
    string? Details);

public sealed record AuditTrailResponse(IReadOnlyList<AuditTrailEntryResponse> Entries);

public sealed record ManualReviewActionRequest(string? Reason);
