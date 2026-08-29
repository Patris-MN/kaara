using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PTS.Modules.WorkManagement;

public sealed class WorkTaskReadStateConfiguration : IEntityTypeConfiguration<WorkTaskReadState>
{
    public void Configure(EntityTypeBuilder<WorkTaskReadState> builder)
    {
        builder.ToTable("task_read_states");
        builder.HasKey(state => new { state.TenantId, state.TaskId, state.MembershipId })
            .HasName("pk_task_read_states");
        builder.Property(state => state.TenantId).HasColumnName("tenant_id");
        builder.Property(state => state.TaskId).HasColumnName("task_id");
        builder.Property(state => state.MembershipId).HasColumnName("membership_id");
        builder.Property(state => state.LastViewedAtUtc).HasColumnName("last_viewed_at_utc").IsRequired();

        builder.HasOne<WorkTask>()
            .WithMany()
            .HasForeignKey(state => new { state.TenantId, state.TaskId })
            .HasPrincipalKey(task => new { task.TenantId, task.Id })
            .HasConstraintName("fk_task_read_states_tasks_tenant_id_task_id")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
