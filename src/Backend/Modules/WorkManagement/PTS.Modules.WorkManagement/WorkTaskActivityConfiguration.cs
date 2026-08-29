using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PTS.Modules.WorkManagement;

public sealed class WorkTaskActivityConfiguration : IEntityTypeConfiguration<WorkTaskActivity>
{
    public const int ValueMaxLength = 4000;

    public void Configure(EntityTypeBuilder<WorkTaskActivity> builder)
    {
        builder.ToTable(
            "task_activities",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_task_activities_event_type",
                    "event_type IN ('TaskCreated','TitleChanged','DescriptionChanged','PriorityChanged','DeadlineChanged','StatusChanged','AssigneeChanged','TagAdded','TagRemoved','CommentAdded','CommentEdited','CommentDeleted','TaskReopened')");
            });

        builder.HasKey(activity => activity.Id).HasName("pk_task_activities");
        builder.Property(activity => activity.Id).HasColumnName("id");
        builder.Property(activity => activity.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(activity => activity.TaskId).HasColumnName("task_id").IsRequired();
        builder.Property(activity => activity.ActorMembershipId).HasColumnName("actor_membership_id").IsRequired();
        builder.Property(activity => activity.EventType)
            .HasColumnName("event_type")
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();
        builder.Property(activity => activity.OldValue)
            .HasColumnName("old_value")
            .HasMaxLength(ValueMaxLength);
        builder.Property(activity => activity.NewValue)
            .HasColumnName("new_value")
            .HasMaxLength(ValueMaxLength);
        builder.Property(activity => activity.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();

        builder.HasOne<WorkTask>()
            .WithMany()
            .HasForeignKey(activity => new { activity.TenantId, activity.TaskId })
            .HasPrincipalKey(task => new { task.TenantId, task.Id })
            .HasConstraintName("fk_task_activities_tasks_tenant_id_task_id")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(activity => new { activity.TenantId, activity.TaskId, activity.CreatedAtUtc })
            .HasDatabaseName("ix_task_activities_tenant_task_created");
    }
}
