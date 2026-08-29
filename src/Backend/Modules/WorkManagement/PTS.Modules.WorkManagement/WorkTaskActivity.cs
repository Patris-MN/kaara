namespace PTS.Modules.WorkManagement;

public sealed class WorkTaskActivity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid TaskId { get; set; }

    public Guid ActorMembershipId { get; set; }

    public WorkTaskActivityType EventType { get; set; }

    public string? OldValue { get; set; }

    public string? NewValue { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}
