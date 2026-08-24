using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PTS.Modules.WorkManagement;

public sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("projects");

        builder.HasKey(p => p.Id).HasName("pk_projects");
        builder.Property(p => p.Id).HasColumnName("id");
        builder.Property(p => p.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(p => p.WorkspaceId).HasColumnName("workspace_id").IsRequired();
        builder.Property(p => p.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(p => p.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();

        builder.HasIndex(p => p.TenantId).HasDatabaseName("ix_projects_tenant_id");
        builder.HasIndex(p => p.WorkspaceId).HasDatabaseName("ix_projects_workspace_id");

        builder.HasOne<Workspace>()
            .WithMany()
            .HasForeignKey(p => new { p.TenantId, p.WorkspaceId })
            .HasPrincipalKey(w => new { w.TenantId, w.Id })
            .HasConstraintName("fk_projects_workspaces_tenant_id_workspace_id")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
