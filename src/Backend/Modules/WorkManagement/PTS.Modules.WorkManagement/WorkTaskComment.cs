namespace PTS.Modules.WorkManagement;

public sealed class WorkTaskComment
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid TaskId { get; set; }

    public Guid AuthorMembershipId { get; set; }

    public required string Body { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
