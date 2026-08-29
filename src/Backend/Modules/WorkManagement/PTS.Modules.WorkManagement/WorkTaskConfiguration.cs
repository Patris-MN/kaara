using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PTS.Modules.WorkManagement;

public sealed class WorkTaskConfiguration : IEntityTypeConfiguration<WorkTask>
{
    public const int TitleMaxLength = 200;
    public const int DescriptionMaxLength = 4000;

    public void Configure(EntityTypeBuilder<WorkTask> builder)
    {
        builder.ToTable(
            "tasks",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_tasks_status",
                    "status IN ('Open', 'InProgress', 'Waiting', 'Resolved', 'Closed')");
                table.HasCheckConstraint(
                    "ck_tasks_priority",
                    "priority IN ('Low', 'Normal', 'High', 'Urgent')");
            });

        builder.HasKey(task => task.Id).HasName("pk_tasks");
        builder.Property(task => task.Id).HasColumnName("id");
        builder.Property(task => task.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(task => task.WorkspaceId).HasColumnName("workspace_id").IsRequired();
        builder.Property(task => task.ProjectId).HasColumnName("project_id").IsRequired();
        builder.Property(task => task.Title)
            .HasColumnName("title")
            .HasMaxLength(TitleMaxLength)
            .IsRequired();
        builder.Property(task => task.Description)
            .HasColumnName("description")
            .HasMaxLength(DescriptionMaxLength);
        builder.Property(task => task.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(task => task.Priority)
            .HasColumnName("priority")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(task => task.DueDate).HasColumnName("due_date").HasColumnType("date");
        builder.Property(task => task.CreatedByMembershipId).HasColumnName("created_by_membership_id").IsRequired();
        builder.Property(task => task.AssignedMembershipId).HasColumnName("assigned_membership_id");
        builder.Property(task => task.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(task => task.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();

        builder.HasAlternateKey(task => new { task.TenantId, task.Id })
            .HasName("ak_tasks_tenant_id_id");
        builder.HasIndex(task => new { task.TenantId, task.ProjectId })
            .HasDatabaseName("ix_tasks_tenant_project");
        builder.HasIndex(task => new { task.TenantId, task.WorkspaceId })
            .HasDatabaseName("ix_tasks_tenant_workspace");
        builder.HasIndex(task => new { task.TenantId, task.AssignedMembershipId })
            .HasDatabaseName("ix_tasks_tenant_assignee");

        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(task => new { task.TenantId, task.WorkspaceId, task.ProjectId })
            .HasPrincipalKey(project => new { project.TenantId, project.WorkspaceId, project.Id })
            .HasConstraintName("fk_tasks_projects_tenant_id_workspace_id_project_id")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
