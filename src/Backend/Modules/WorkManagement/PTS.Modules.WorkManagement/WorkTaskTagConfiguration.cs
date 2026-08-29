using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PTS.Modules.WorkManagement;

public sealed class WorkTaskTagConfiguration : IEntityTypeConfiguration<WorkTaskTag>
{
    public void Configure(EntityTypeBuilder<WorkTaskTag> builder)
    {
        builder.ToTable("task_tags");

        builder.HasKey(link => new { link.TenantId, link.TaskId, link.TagId })
            .HasName("pk_task_tags");
        builder.Property(link => link.TenantId).HasColumnName("tenant_id");
        builder.Property(link => link.TaskId).HasColumnName("task_id");
        builder.Property(link => link.TagId).HasColumnName("tag_id");

        builder.HasOne<WorkTask>()
            .WithMany()
            .HasForeignKey(link => new { link.TenantId, link.TaskId })
            .HasPrincipalKey(task => new { task.TenantId, task.Id })
            .HasConstraintName("fk_task_tags_tasks_tenant_id_task_id")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<WorkTag>()
            .WithMany()
            .HasForeignKey(link => new { link.TenantId, link.TagId })
            .HasPrincipalKey(tag => new { tag.TenantId, tag.Id })
            .HasConstraintName("fk_task_tags_tags_tenant_id_tag_id")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
