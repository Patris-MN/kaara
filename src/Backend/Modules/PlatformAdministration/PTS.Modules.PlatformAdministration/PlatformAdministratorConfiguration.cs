using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PTS.Modules.PlatformAdministration;

public sealed class PlatformAdministratorConfiguration : IEntityTypeConfiguration<PlatformAdministrator>
{
    public void Configure(EntityTypeBuilder<PlatformAdministrator> builder)
    {
        builder.ToTable("platform_administrators");

        builder.HasKey(a => a.UserId).HasName("pk_platform_administrators");
        builder.Property(a => a.UserId).HasColumnName("user_id");
        builder.Property(a => a.GrantedAtUtc)
            .HasColumnName("granted_at_utc")
            .IsRequired();
    }
}
