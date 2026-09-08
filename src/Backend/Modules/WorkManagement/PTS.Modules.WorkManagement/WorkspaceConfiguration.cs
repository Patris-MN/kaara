using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PTS.Modules.WorkManagement;

public sealed class WorkspaceConfiguration : IEntityTypeConfiguration<Workspace>
{
    public void Configure(EntityTypeBuilder<Workspace> builder)
    {
        builder.ToTable("workspaces");

        builder.HasKey(w => w.Id).HasName("pk_workspaces");
        builder.Property(w => w.Id).HasColumnName("id");
        builder.Property(w => w.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(w => w.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(w => w.NameNormalized)
            .HasColumnName("name_normalized")
            .HasMaxLength(200)
            .IsRequired();
        builder.Property(w => w.Description).HasColumnName("description").HasMaxLength(500);
        builder.Property(w => w.StartDate).HasColumnName("start_date");
        builder.Property(w => w.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(w => w.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.HasIndex(w => w.TenantId).HasDatabaseName("ix_workspaces_tenant_id");
        builder.HasIndex(w => new { w.TenantId, w.NameNormalized })
            .IsUnique()
            .HasDatabaseName("ux_workspaces_tenant_name_normalized");

        // Alternate key so projects can FK to (tenant_id, id) and cannot
        // point at a workspace owned by a different tenant.
        builder.HasAlternateKey(w => new { w.TenantId, w.Id })
            .HasName("ak_workspaces_tenant_id_id");
    }
}
