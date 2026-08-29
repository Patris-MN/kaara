namespace PTS.Modules.WorkManagement;

/// <summary>
/// Central Task status transition rules. Callers must not compare status
/// strings ad hoc for write authorization.
/// </summary>
public sealed class TaskStatusWorkflow
{
    public bool IsClosed(WorkTaskStatus status) => status == WorkTaskStatus.Closed;

    public bool CanTransition(bool isCreator, bool isCurrentAssignee, WorkTaskStatus from, WorkTaskStatus to)
    {
        if (from == to)
        {
            return true;
        }

        if (from == WorkTaskStatus.Closed)
        {
            return isCreator && to == WorkTaskStatus.Open;
        }

        if (to == WorkTaskStatus.Closed)
        {
            return isCreator;
        }

        return (isCreator || isCurrentAssignee) && to is
            WorkTaskStatus.Open or
            WorkTaskStatus.InProgress or
            WorkTaskStatus.Waiting or
            WorkTaskStatus.Resolved;
    }

    public static WorkTaskStatus ParseOrDefault(string? value, out bool valid)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            valid = true;
            return WorkTaskStatus.Open;
        }

        if (string.Equals(value, "Todo", StringComparison.OrdinalIgnoreCase))
        {
            valid = true;
            return WorkTaskStatus.Open;
        }

        if (string.Equals(value, "Done", StringComparison.OrdinalIgnoreCase))
        {
            valid = true;
            return WorkTaskStatus.Closed;
        }

        valid = Enum.TryParse(value, ignoreCase: true, out WorkTaskStatus status) && Enum.IsDefined(status);
        return valid ? status : WorkTaskStatus.Open;
    }
}
