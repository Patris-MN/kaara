using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PTS.Modules.Identity;

public sealed class UserCredentialConfiguration : IEntityTypeConfiguration<UserCredential>
{
    public void Configure(EntityTypeBuilder<UserCredential> builder)
    {
        builder.ToTable("user_credentials");

        builder.HasKey(c => c.UserId).HasName("pk_user_credentials");
        builder.Property(c => c.UserId).HasColumnName("user_id");

        builder.Property(c => c.Email)
            .HasColumnName("email")
            .HasMaxLength(320)
            .IsRequired();

        builder.Property(c => c.PasswordHash)
            .HasColumnName("password_hash")
            .HasMaxLength(500)
            .IsRequired();

        builder.HasIndex(c => c.Email)
            .HasDatabaseName("ix_user_credentials_email")
            .IsUnique();
    }
}
