using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PTS.Modules.Identity;

/// <summary>
/// Maps only <see cref="User"/>'s own columns/keys/indexes. Cross-module
/// relationships (e.g. Membership → User) are wired in the composition root's
/// DbContext, not here — see
/// docs/architecture/decisions/0004-tenancy-ports-and-adapters-for-persistence.md.
/// </summary>
public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        // Explicit snake_case column names (idiomatic Postgres, and matches the
        // snake_case table names) so that raw SQL written elsewhere (RLS
        // policies, grants) never has to guess EF Core's default quoted
        // PascalCase column naming — see
        // docs/architecture/decisions/0002-row-level-security-for-tenant-isolation.md
        // for why this actually matters (a mismatch here previously broke a
        // migration against a real database).
        builder.ToTable("users");

        builder.HasKey(u => u.Id).HasName("pk_users");
        builder.Property(u => u.Id).HasColumnName("id");

        builder.Property(u => u.Email)
            .HasColumnName("email")
            .HasMaxLength(320)
            .IsRequired();

        builder.Property(u => u.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(u => u.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.HasIndex(u => u.Email)
            .IsUnique();
    }
}
