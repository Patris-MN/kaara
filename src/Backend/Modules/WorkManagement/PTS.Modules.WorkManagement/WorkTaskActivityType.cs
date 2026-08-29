namespace PTS.Modules.WorkManagement;

public enum WorkTaskActivityType
{
    TaskCreated,
    TitleChanged,
    DescriptionChanged,
    PriorityChanged,
    DeadlineChanged,
    StatusChanged,
    AssigneeChanged,
    TagAdded,
    TagRemoved,
    CommentAdded,
    CommentEdited,
    CommentDeleted,
    TaskReopened,
}
