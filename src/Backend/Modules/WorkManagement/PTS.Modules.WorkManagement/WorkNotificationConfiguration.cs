using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PTS.Modules.WorkManagement;

public sealed class WorkNotificationConfiguration : IEntityTypeConfiguration<WorkNotification>
{
    public void Configure(EntityTypeBuilder<WorkNotification> builder)
    {
        builder.ToTable(
            "notifications",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_notifications_type",
                    "type IN ('TaskAssigned','TaskReassigned','TaskCommentAdded','TaskPriorityChanged','TaskDeadlineChanged','TaskStatusChanged','TaskTagChanged','TaskUpdated','TaskClosed','TaskReopened')");
            });

        builder.HasKey(notification => notification.Id).HasName("pk_notifications");
        builder.Property(notification => notification.Id).HasColumnName("id");
        builder.Property(notification => notification.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(notification => notification.RecipientMembershipId)
            .HasColumnName("recipient_membership_id")
            .IsRequired();
        builder.Property(notification => notification.Type)
            .HasColumnName("type")
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();
        builder.Property(notification => notification.TaskId).HasColumnName("task_id");
        builder.Property(notification => notification.WorkspaceId).HasColumnName("workspace_id");
        builder.Property(notification => notification.ProjectId).HasColumnName("project_id");
        builder.Property(notification => notification.IsRead).HasColumnName("is_read").IsRequired();
        builder.Property(notification => notification.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();

        builder.HasIndex(notification => new { notification.TenantId, notification.RecipientMembershipId, notification.IsRead })
            .HasDatabaseName("ix_notifications_tenant_recipient_unread");
    }
}
