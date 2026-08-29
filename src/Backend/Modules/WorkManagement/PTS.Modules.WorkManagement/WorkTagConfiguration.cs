using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PTS.Modules.WorkManagement;

public sealed class WorkTagConfiguration : IEntityTypeConfiguration<WorkTag>
{
    public const int NameMaxLength = 40;

    public void Configure(EntityTypeBuilder<WorkTag> builder)
    {
        builder.ToTable("tags");

        builder.HasKey(tag => tag.Id).HasName("pk_tags");
        builder.Property(tag => tag.Id).HasColumnName("id");
        builder.Property(tag => tag.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(tag => tag.Name)
            .HasColumnName("name")
            .HasMaxLength(NameMaxLength)
            .IsRequired();
        builder.Property(tag => tag.NormalizedName)
            .HasColumnName("normalized_name")
            .HasMaxLength(NameMaxLength)
            .IsRequired();
        builder.Property(tag => tag.CreatedByMembershipId).HasColumnName("created_by_membership_id").IsRequired();
        builder.Property(tag => tag.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();

        builder.HasAlternateKey(tag => new { tag.TenantId, tag.Id })
            .HasName("ak_tags_tenant_id_id");
        builder.HasIndex(tag => new { tag.TenantId, tag.NormalizedName })
            .IsUnique()
            .HasDatabaseName("ix_tags_tenant_normalized_name");
    }

    public static string? NormalizeName(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Length > NameMaxLength)
        {
            return null;
        }

        return trimmed.ToUpperInvariant();
    }
}
