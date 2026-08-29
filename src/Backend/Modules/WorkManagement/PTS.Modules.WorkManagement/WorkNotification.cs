namespace PTS.Modules.WorkManagement;

/// <summary>
/// In-app notification targeted at one tenant Membership. Structured type plus
/// related Task ids let the UI localize the message; English text is not stored.
/// Self-assignment does not create a row.
/// </summary>
public sealed class WorkNotification
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid RecipientMembershipId { get; set; }

    public WorkNotificationType Type { get; set; }

    public Guid? TaskId { get; set; }

    public Guid? WorkspaceId { get; set; }

    public Guid? ProjectId { get; set; }

    public bool IsRead { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}
