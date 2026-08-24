using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PTS.Host.Persistence.Testing;

/// <summary>
/// Maps <see cref="TenantIsolationTestRecord"/>'s own columns. Row-Level
/// Security enable/force/policy and the app_role grant are NOT expressible
/// through EF Core's fluent API and are instead added as raw SQL in the
/// initial migration — see Persistence/Migrations and
/// docs/architecture/decisions/0002-row-level-security-for-tenant-isolation.md.
/// </summary>
public sealed class TenantIsolationTestRecordConfiguration : IEntityTypeConfiguration<TenantIsolationTestRecord>
{
    public void Configure(EntityTypeBuilder<TenantIsolationTestRecord> builder)
    {
        // Explicit snake_case column names — matters especially here, since the
        // RLS policy migration references `tenant_id` directly in raw SQL.
        builder.ToTable("tenant_isolation_test_records");

        builder.HasKey(r => r.Id).HasName("pk_tenant_isolation_test_records");
        builder.Property(r => r.Id).HasColumnName("id");
        builder.Property(r => r.TenantId).HasColumnName("tenant_id");

        builder.Property(r => r.Value)
            .HasColumnName("value")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(r => r.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.HasIndex(r => r.TenantId)
            .HasDatabaseName("ix_tenant_isolation_test_records_tenant_id");
    }
}
