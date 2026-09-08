namespace PTS.Modules.Tenancy;

/// <summary>
/// Raised when the authenticated user already belongs to an Active organization
/// whose display name matches the requested name (case/whitespace insensitive).
/// </summary>
public sealed class TenantNameConflictException : Exception
{
    public TenantNameConflictException(string? existingName)
        : base("An organization with this name already exists for the user.")
    {
        ExistingName = existingName;
    }

    public string? ExistingName { get; }
}
