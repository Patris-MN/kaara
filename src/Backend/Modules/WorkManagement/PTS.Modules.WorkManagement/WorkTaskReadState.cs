namespace PTS.Modules.WorkManagement;

public sealed class WorkTaskReadState
{
    public Guid TenantId { get; set; }

    public Guid TaskId { get; set; }

    public Guid MembershipId { get; set; }

    public DateTimeOffset LastViewedAtUtc { get; set; }
}
