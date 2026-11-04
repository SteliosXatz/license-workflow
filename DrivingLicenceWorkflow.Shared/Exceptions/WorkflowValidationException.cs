namespace DrivingLicenceWorkflow.Shared.Exceptions;

/// <summary>
/// A permanent workflow validation failure (missing data or an invalid step result).
/// Retrying cannot fix it, so message retry ignores this type.
/// </summary>
public sealed class WorkflowValidationException : Exception
{
    public WorkflowValidationException(string message)
        : base(message)
    {
    }

    public WorkflowValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
