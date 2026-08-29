using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PTS.Modules.WorkManagement;

public sealed class WorkspaceAccessConfiguration : IEntityTypeConfiguration<WorkspaceAccess>
{
    public void Configure(EntityTypeBuilder<WorkspaceAccess> builder)
    {
        builder.ToTable(
            "workspace_access",
            table => table.HasCheckConstraint(
                "ck_workspace_access_access_level",
                "access_level IN ('View', 'Edit')"));

        builder.HasKey(access => access.Id).HasName("pk_workspace_access");
        builder.Property(access => access.Id).HasColumnName("id");
        builder.Property(access => access.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(access => access.MembershipId).HasColumnName("membership_id").IsRequired();
        builder.Property(access => access.WorkspaceId).HasColumnName("workspace_id").IsRequired();
        builder.Property(access => access.AccessLevel)
            .HasColumnName("access_level")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(access => access.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(access => access.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();

        builder.HasIndex(access => new { access.TenantId, access.MembershipId, access.WorkspaceId })
            .HasDatabaseName("ix_workspace_access_tenant_membership_workspace")
            .IsUnique();
        builder.HasIndex(access => new { access.TenantId, access.WorkspaceId })
            .HasDatabaseName("ix_workspace_access_tenant_workspace");

        builder.HasOne<Workspace>()
            .WithMany()
            .HasForeignKey(access => new { access.TenantId, access.WorkspaceId })
            .HasPrincipalKey(workspace => new { workspace.TenantId, workspace.Id })
            .HasConstraintName("fk_workspace_access_workspaces_tenant_id_workspace_id")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
