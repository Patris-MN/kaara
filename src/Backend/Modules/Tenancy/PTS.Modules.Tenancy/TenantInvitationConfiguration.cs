using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PTS.Modules.Tenancy;

public class TenantInvitationConfiguration : IEntityTypeConfiguration<TenantInvitation>
{
    public void Configure(EntityTypeBuilder<TenantInvitation> builder)
    {
        builder.ToTable("tenant_invitations");

        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasColumnName("id");
        builder.Property(item => item.TenantId).HasColumnName("tenant_id");
        builder.Property(item => item.InvitedEmail)
            .HasColumnName("invited_email")
            .HasMaxLength(320)
            .IsRequired();
        builder.Property(item => item.Role)
            .HasColumnName("role")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(item => item.TokenHash)
            .HasColumnName("token_hash")
            .HasMaxLength(64)
            .IsRequired();
        builder.Property(item => item.ExpiresAtUtc).HasColumnName("expires_at_utc");
        builder.Property(item => item.UsedAtUtc).HasColumnName("used_at_utc");
        builder.Property(item => item.RevokedAtUtc).HasColumnName("revoked_at_utc");
        builder.Property(item => item.CreatedByMembershipId).HasColumnName("created_by_membership_id");
        builder.Property(item => item.OrganizationName)
            .HasColumnName("organization_name")
            .HasMaxLength(200)
            .IsRequired();
        builder.Property(item => item.InviterDisplayName)
            .HasColumnName("inviter_display_name")
            .HasMaxLength(200);
        builder.Property(item => item.InviteeUserId).HasColumnName("invitee_user_id");
        builder.Property(item => item.MembershipId).HasColumnName("membership_id");
        builder.Property(item => item.CreatedAtUtc).HasColumnName("created_at_utc");

        builder.HasIndex(item => item.TokenHash)
            .IsUnique()
            .HasDatabaseName("ux_tenant_invitations_token_hash");

        builder.HasIndex(item => new { item.TenantId, item.InvitedEmail })
            .HasDatabaseName("ix_tenant_invitations_tenant_email");
    }
}
