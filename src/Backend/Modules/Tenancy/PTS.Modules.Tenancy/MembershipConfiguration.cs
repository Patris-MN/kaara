using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PTS.Modules.Tenancy;

/// <summary>
/// Maps only Membership's own columns/keys/indexes. The foreign keys to
/// <c>users</c> and <c>tenants</c> are declared in the composition root's
/// DbContext — see docs/architecture/decisions/0004-....md.
/// </summary>
public sealed class MembershipConfiguration : IEntityTypeConfiguration<Membership>
{
    public void Configure(EntityTypeBuilder<Membership> builder)
    {
        // Explicit snake_case column names — see UserConfiguration for why.
        builder.ToTable("memberships");

        builder.HasKey(m => m.Id).HasName("pk_memberships");
        builder.Property(m => m.Id).HasColumnName("id");
        builder.Property(m => m.UserId).HasColumnName("user_id");
        builder.Property(m => m.TenantId).HasColumnName("tenant_id");

        builder.Property(m => m.Role)
            .HasColumnName("role")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(m => m.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(m => m.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        // A user may hold at most one Membership per tenant — re-inviting or
        // reactivating must update the existing row, never create a duplicate.
        builder.HasIndex(m => new { m.UserId, m.TenantId })
            .HasDatabaseName("ix_memberships_user_id_tenant_id")
            .IsUnique();

        builder.HasIndex(m => m.TenantId)
            .HasDatabaseName("ix_memberships_tenant_id");
    }
}
