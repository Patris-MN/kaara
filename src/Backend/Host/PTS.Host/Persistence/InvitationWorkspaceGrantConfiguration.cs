using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PTS.Host.Persistence;

public class InvitationWorkspaceGrantConfiguration : IEntityTypeConfiguration<InvitationWorkspaceGrant>
{
    public void Configure(EntityTypeBuilder<InvitationWorkspaceGrant> builder)
    {
        builder.ToTable("invitation_workspace_grants");

        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasColumnName("id");
        builder.Property(item => item.TenantId).HasColumnName("tenant_id");
        builder.Property(item => item.InvitationId).HasColumnName("invitation_id");
        builder.Property(item => item.WorkspaceId).HasColumnName("workspace_id");
        builder.Property(item => item.WorkspaceName)
            .HasColumnName("workspace_name")
            .HasMaxLength(200)
            .IsRequired();
        builder.Property(item => item.AccessLevel)
            .HasColumnName("access_level")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.HasIndex(item => item.InvitationId)
            .HasDatabaseName("ix_invitation_workspace_grants_invitation_id");
    }
}
