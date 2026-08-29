using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PTS.Modules.WorkManagement;

public sealed class WorkTaskCommentConfiguration : IEntityTypeConfiguration<WorkTaskComment>
{
    public const int BodyMaxLength = 4000;

    public void Configure(EntityTypeBuilder<WorkTaskComment> builder)
    {
        builder.ToTable("task_comments");
        builder.HasKey(comment => comment.Id).HasName("pk_task_comments");
        builder.Property(comment => comment.Id).HasColumnName("id");
        builder.Property(comment => comment.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(comment => comment.TaskId).HasColumnName("task_id").IsRequired();
        builder.Property(comment => comment.AuthorMembershipId).HasColumnName("author_membership_id").IsRequired();
        builder.Property(comment => comment.Body)
            .HasColumnName("body")
            .HasMaxLength(BodyMaxLength)
            .IsRequired();
        builder.Property(comment => comment.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(comment => comment.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.HasOne<WorkTask>()
            .WithMany()
            .HasForeignKey(comment => new { comment.TenantId, comment.TaskId })
            .HasPrincipalKey(task => new { task.TenantId, task.Id })
            .HasConstraintName("fk_task_comments_tasks_tenant_id_task_id")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(comment => new { comment.TenantId, comment.TaskId, comment.CreatedAtUtc })
            .HasDatabaseName("ix_task_comments_tenant_task_created");
    }
}
