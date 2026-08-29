namespace PTS.Modules.WorkManagement;

/// <summary>
/// Fixed Phase 6 priority set. <c>Medium</c> was renamed to <c>Normal</c>
/// (same default slot). Existing rows are rewritten by the
/// TaskPriorityDeadline migration. The API still accepts the legacy
/// <c>Medium</c> string and stores it as <see cref="Normal"/>.
/// </summary>
public enum WorkTaskPriority
{
    Low,
    Normal,
    High,
    Urgent,
}
