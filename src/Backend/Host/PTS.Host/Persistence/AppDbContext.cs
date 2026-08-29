using Microsoft.EntityFrameworkCore;
using PTS.Host.Persistence.Testing;
using PTS.Modules.Identity;
using PTS.Modules.PlatformAdministration;
using PTS.Modules.Tenancy;
using PTS.Modules.WorkManagement;

namespace PTS.Host.Persistence;

/// <summary>
/// The single composed EF Core DbContext for the whole application (see
/// architecture-charter.md §2.2: PTS.Host is the composition root). Each
/// module owns the mapping for its own entities' own columns/keys/indexes
/// via <c>IEntityTypeConfiguration&lt;T&gt;</c>; cross-module relationships
/// (foreign keys between two different modules' entities) are wired here
/// instead, because only the Host is allowed to know about more than one
/// module at a time — see
/// docs/architecture/decisions/0004-tenancy-ports-and-adapters-for-persistence.md.
///
/// This is intentionally the ONE and only DbContext. No generic repository
/// wraps it (see decisions/0003-avoid-cqrs-mediatr-repository-abstractions.md).
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();

    public DbSet<UserCredential> UserCredentials => Set<UserCredential>();

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<Membership> Memberships => Set<Membership>();

    /// <summary>Security-proof scaffolding only — see
    /// <see cref="TenantIsolationTestRecord"/> for why this isn't a business table.</summary>
    public DbSet<TenantIsolationTestRecord> TenantIsolationTestRecords => Set<TenantIsolationTestRecord>();

    public DbSet<Workspace> Workspaces => Set<Workspace>();

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<WorkspaceAccess> WorkspaceAccess => Set<WorkspaceAccess>();

    public DbSet<WorkTask> WorkTasks => Set<WorkTask>();

    public DbSet<WorkTag> WorkTags => Set<WorkTag>();

    public DbSet<WorkTaskTag> WorkTaskTags => Set<WorkTaskTag>();

    public DbSet<WorkNotification> WorkNotifications => Set<WorkNotification>();

    public DbSet<WorkTaskComment> WorkTaskComments => Set<WorkTaskComment>();

    public DbSet<WorkTaskActivity> WorkTaskActivities => Set<WorkTaskActivity>();

    public DbSet<WorkTaskReadState> WorkTaskReadStates => Set<WorkTaskReadState>();

    public DbSet<PlatformAdministrator> PlatformAdministrators => Set<PlatformAdministrator>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new UserConfiguration());
        modelBuilder.ApplyConfiguration(new UserCredentialConfiguration());
        modelBuilder.ApplyConfiguration(new TenantConfiguration());
        modelBuilder.ApplyConfiguration(new MembershipConfiguration());
        modelBuilder.ApplyConfiguration(new TenantIsolationTestRecordConfiguration());
        modelBuilder.ApplyConfiguration(new WorkspaceConfiguration());
        modelBuilder.ApplyConfiguration(new ProjectConfiguration());
        modelBuilder.ApplyConfiguration(new WorkspaceAccessConfiguration());
        modelBuilder.ApplyConfiguration(new WorkTaskConfiguration());
        modelBuilder.ApplyConfiguration(new WorkTagConfiguration());
        modelBuilder.ApplyConfiguration(new WorkTaskTagConfiguration());
        modelBuilder.ApplyConfiguration(new WorkNotificationConfiguration());
        modelBuilder.ApplyConfiguration(new WorkTaskCommentConfiguration());
        modelBuilder.ApplyConfiguration(new WorkTaskActivityConfiguration());
        modelBuilder.ApplyConfiguration(new WorkTaskReadStateConfiguration());
        modelBuilder.ApplyConfiguration(new PlatformAdministratorConfiguration());

        modelBuilder.Entity<UserCredential>()
            .HasOne<User>()
            .WithOne()
            .HasForeignKey<UserCredential>(c => c.UserId)
            .HasConstraintName("fk_user_credentials_users_user_id")
            .OnDelete(DeleteBehavior.Cascade);

        // Cross-module foreign keys: composed here, not inside Tenancy, so that
        // Tenancy never needs a compile-time reference to Identity just to
        // declare "a Membership belongs to a User".
        modelBuilder.Entity<Membership>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(m => m.UserId)
            .HasConstraintName("fk_memberships_users_user_id")
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Membership>()
            .HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(m => m.TenantId)
            .HasConstraintName("fk_memberships_tenants_tenant_id")
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<TenantIsolationTestRecord>()
            .HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(r => r.TenantId)
            .HasConstraintName("fk_tenant_isolation_test_records_tenants_tenant_id")
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Workspace>()
            .HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(w => w.TenantId)
            .HasConstraintName("fk_workspaces_tenants_tenant_id")
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Project>()
            .HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(p => p.TenantId)
            .HasConstraintName("fk_projects_tenants_tenant_id")
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<WorkspaceAccess>()
            .HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(access => access.TenantId)
            .HasConstraintName("fk_workspace_access_tenants_tenant_id")
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<WorkTask>()
            .HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(task => task.TenantId)
            .HasConstraintName("fk_tasks_tenants_tenant_id")
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<WorkTask>()
            .HasOne<Membership>()
            .WithMany()
            .HasForeignKey(task => new { task.TenantId, task.CreatedByMembershipId })
            .HasPrincipalKey(membership => new { membership.TenantId, membership.Id })
            .HasConstraintName("fk_tasks_memberships_tenant_id_created_by_membership_id")
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<WorkTask>()
            .HasOne<Membership>()
            .WithMany()
            .HasForeignKey(task => new { task.TenantId, task.AssignedMembershipId })
            .HasPrincipalKey(membership => new { membership.TenantId, membership.Id })
            .IsRequired(false)
            .HasConstraintName("fk_tasks_memberships_tenant_id_assigned_membership_id")
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<WorkTag>()
            .HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(tag => tag.TenantId)
            .HasConstraintName("fk_tags_tenants_tenant_id")
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<WorkTag>()
            .HasOne<Membership>()
            .WithMany()
            .HasForeignKey(tag => new { tag.TenantId, tag.CreatedByMembershipId })
            .HasPrincipalKey(membership => new { membership.TenantId, membership.Id })
            .HasConstraintName("fk_tags_memberships_tenant_id_created_by_membership_id")
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<WorkNotification>()
            .HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(notification => notification.TenantId)
            .HasConstraintName("fk_notifications_tenants_tenant_id")
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<WorkNotification>()
            .HasOne<Membership>()
            .WithMany()
            .HasForeignKey(notification => new { notification.TenantId, notification.RecipientMembershipId })
            .HasPrincipalKey(membership => new { membership.TenantId, membership.Id })
            .HasConstraintName("fk_notifications_memberships_tenant_id_recipient_membership_id")
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<WorkNotification>()
            .HasOne<WorkTask>()
            .WithMany()
            .HasForeignKey(notification => new { notification.TenantId, notification.TaskId })
            .HasPrincipalKey(task => new { task.TenantId, task.Id })
            .IsRequired(false)
            .HasConstraintName("fk_notifications_tasks_tenant_id_task_id")
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<WorkTaskComment>()
            .HasOne<Membership>()
            .WithMany()
            .HasForeignKey(comment => new { comment.TenantId, comment.AuthorMembershipId })
            .HasPrincipalKey(membership => new { membership.TenantId, membership.Id })
            .HasConstraintName("fk_task_comments_memberships_tenant_id_author_membership_id")
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<WorkTaskActivity>()
            .HasOne<Membership>()
            .WithMany()
            .HasForeignKey(activity => new { activity.TenantId, activity.ActorMembershipId })
            .HasPrincipalKey(membership => new { membership.TenantId, membership.Id })
            .HasConstraintName("fk_task_activities_memberships_tenant_id_actor_membership_id")
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<WorkTaskReadState>()
            .HasOne<Membership>()
            .WithMany()
            .HasForeignKey(state => new { state.TenantId, state.MembershipId })
            .HasPrincipalKey(membership => new { membership.TenantId, membership.Id })
            .HasConstraintName("fk_task_read_states_memberships_tenant_id_membership_id")
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<WorkspaceAccess>()
            .HasOne<Membership>()
            .WithMany()
            .HasForeignKey(access => new { access.TenantId, access.MembershipId })
            .HasPrincipalKey(membership => new { membership.TenantId, membership.Id })
            .HasConstraintName("fk_workspace_access_memberships_tenant_id_membership_id")
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PlatformAdministrator>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .HasConstraintName("fk_platform_administrators_users_user_id")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
