using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PTS.Host.Persistence;
using PTS.Modules.WorkManagement;

namespace PTS.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class TaskCollaborationRlsTests
{
    private readonly PostgresFixture _postgres;

    public TaskCollaborationRlsTests(PostgresFixture postgres)
    {
        _postgres = postgres;
    }

    [SkippableFact]
    public async Task Tags_and_task_tags_are_hidden_across_tenants_and_reject_cross_tenant_links()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var factory = _postgres.Services.GetRequiredService<TestDataFactory>();
        var (userA, tenantA) = await factory.CreateUserWithTenantAsync("TagA");
        var (userB, tenantB) = await factory.CreateUserWithTenantAsync("TagB");
        var membershipA = await factory.GetMembershipIdAsync(userA, tenantA);
        var membershipB = await factory.GetMembershipIdAsync(userB, tenantB);

        Guid taskA;
        Guid tagA;
        Guid tagB;
        await using (var sessionA = await ScopedTenantSession.OpenAsync(_postgres.Services, userA, tenantA))
        {
            var workspace = new Workspace { Id = Guid.NewGuid(), TenantId = tenantA, Name = "TA", CreatedAtUtc = DateTimeOffset.UtcNow };
            sessionA.DbContext.Workspaces.Add(workspace);
            var project = new Project
            {
                Id = Guid.NewGuid(),
                TenantId = tenantA,
                WorkspaceId = workspace.Id,
                Name = "PA",
                CreatedAtUtc = DateTimeOffset.UtcNow,
            };
            sessionA.DbContext.Projects.Add(project);
            var task = new WorkTask
            {
                Id = Guid.NewGuid(),
                TenantId = tenantA,
                WorkspaceId = workspace.Id,
                ProjectId = project.Id,
                Title = "task-A",
                Status = WorkTaskStatus.Open,
                Priority = WorkTaskPriority.Normal,
                CreatedByMembershipId = membershipA,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            };
            taskA = task.Id;
            sessionA.DbContext.WorkTasks.Add(task);
            var tag = new WorkTag
            {
                Id = Guid.NewGuid(),
                TenantId = tenantA,
                Name = "Backend",
                NormalizedName = "BACKEND",
                CreatedByMembershipId = membershipA,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            };
            tagA = tag.Id;
            sessionA.DbContext.WorkTags.Add(tag);
            sessionA.DbContext.WorkTaskTags.Add(new WorkTaskTag
            {
                TenantId = tenantA,
                TaskId = task.Id,
                TagId = tag.Id,
            });
            await sessionA.DbContext.SaveChangesAsync();
            await sessionA.CommitAsync();
        }

        await using (var sessionB = await ScopedTenantSession.OpenAsync(_postgres.Services, userB, tenantB))
        {
            var workspace = new Workspace { Id = Guid.NewGuid(), TenantId = tenantB, Name = "TB", CreatedAtUtc = DateTimeOffset.UtcNow };
            sessionB.DbContext.Workspaces.Add(workspace);
            var project = new Project
            {
                Id = Guid.NewGuid(),
                TenantId = tenantB,
                WorkspaceId = workspace.Id,
                Name = "PB",
                CreatedAtUtc = DateTimeOffset.UtcNow,
            };
            sessionB.DbContext.Projects.Add(project);
            var task = new WorkTask
            {
                Id = Guid.NewGuid(),
                TenantId = tenantB,
                WorkspaceId = workspace.Id,
                ProjectId = project.Id,
                Title = "task-B",
                Status = WorkTaskStatus.Open,
                Priority = WorkTaskPriority.Normal,
                CreatedByMembershipId = membershipB,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            };
            sessionB.DbContext.WorkTasks.Add(task);
            var tag = new WorkTag
            {
                Id = Guid.NewGuid(),
                TenantId = tenantB,
                Name = "Foreign",
                NormalizedName = "FOREIGN",
                CreatedByMembershipId = membershipB,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            };
            tagB = tag.Id;
            sessionB.DbContext.WorkTags.Add(tag);
            await sessionB.DbContext.SaveChangesAsync();
            await sessionB.CommitAsync();
        }

        await using (var readA = await ScopedTenantSession.OpenAsync(_postgres.Services, userA, tenantA))
        {
            var tags = await readA.DbContext.WorkTags.Select(tag => tag.Name).ToListAsync();
            Assert.Contains("Backend", tags);
            Assert.DoesNotContain("Foreign", tags);
            Assert.Single(await readA.DbContext.WorkTaskTags.ToListAsync());
        }

        await using (var sessionA = await ScopedTenantSession.OpenAsync(_postgres.Services, userA, tenantA))
        {
            sessionA.DbContext.WorkTags.Add(new WorkTag
            {
                Id = Guid.NewGuid(),
                TenantId = tenantB,
                Name = "stolen",
                NormalizedName = "STOLEN",
                CreatedByMembershipId = membershipA,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => sessionA.DbContext.SaveChangesAsync());
        }

        await using var crossLink = await ScopedTenantSession.OpenAsync(_postgres.Services, userA, tenantA);
        crossLink.DbContext.WorkTaskTags.Add(new WorkTaskTag
        {
            TenantId = tenantA,
            TaskId = taskA,
            TagId = tagB,
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => crossLink.DbContext.SaveChangesAsync());
        _ = tagA;
    }

    [SkippableFact]
    public async Task Task_cannot_reference_a_foreign_tenant_membership_as_assignee()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var factory = _postgres.Services.GetRequiredService<TestDataFactory>();
        var (userA, tenantA) = await factory.CreateUserWithTenantAsync("AsgA");
        var (userB, tenantB) = await factory.CreateUserWithTenantAsync("AsgB");
        var membershipA = await factory.GetMembershipIdAsync(userA, tenantA);
        var membershipB = await factory.GetMembershipIdAsync(userB, tenantB);

        await using var sessionA = await ScopedTenantSession.OpenAsync(_postgres.Services, userA, tenantA);
        var workspace = new Workspace { Id = Guid.NewGuid(), TenantId = tenantA, Name = "WA", CreatedAtUtc = DateTimeOffset.UtcNow };
        sessionA.DbContext.Workspaces.Add(workspace);
        var project = new Project
        {
            Id = Guid.NewGuid(),
            TenantId = tenantA,
            WorkspaceId = workspace.Id,
            Name = "PA",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        sessionA.DbContext.Projects.Add(project);
        sessionA.DbContext.WorkTasks.Add(new WorkTask
        {
            Id = Guid.NewGuid(),
            TenantId = tenantA,
            WorkspaceId = workspace.Id,
            ProjectId = project.Id,
            Title = "foreign-assignee",
            Status = WorkTaskStatus.Open,
            Priority = WorkTaskPriority.Normal,
            CreatedByMembershipId = membershipA,
            AssignedMembershipId = membershipB,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => sessionA.DbContext.SaveChangesAsync());
    }

    [SkippableFact]
    public async Task Notifications_are_visible_only_to_the_intended_recipient()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var factory = _postgres.Services.GetRequiredService<TestDataFactory>();
        var (userA, tenantA) = await factory.CreateUserWithTenantAsync("NtfA");
        var userB = await factory.CreateUserAsync("NtfB");
        var membershipA = await factory.GetMembershipIdAsync(userA, tenantA);
        var membershipB = await factory.CreateActiveMembershipAsync(userB, tenantA);
        var (userC, tenantC) = await factory.CreateUserWithTenantAsync("NtfC");

        Guid taskId;
        Guid notificationB;
        await using (var sessionA = await ScopedTenantSession.OpenAsync(_postgres.Services, userA, tenantA))
        {
            var workspace = new Workspace { Id = Guid.NewGuid(), TenantId = tenantA, Name = "NA", CreatedAtUtc = DateTimeOffset.UtcNow };
            sessionA.DbContext.Workspaces.Add(workspace);
            var project = new Project
            {
                Id = Guid.NewGuid(),
                TenantId = tenantA,
                WorkspaceId = workspace.Id,
                Name = "NP",
                CreatedAtUtc = DateTimeOffset.UtcNow,
            };
            sessionA.DbContext.Projects.Add(project);
            var task = new WorkTask
            {
                Id = Guid.NewGuid(),
                TenantId = tenantA,
                WorkspaceId = workspace.Id,
                ProjectId = project.Id,
                Title = "notify",
                Status = WorkTaskStatus.Open,
                Priority = WorkTaskPriority.Normal,
                CreatedByMembershipId = membershipA,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            };
            taskId = task.Id;
            sessionA.DbContext.WorkTasks.Add(task);
            var forB = new WorkNotification
            {
                Id = Guid.NewGuid(),
                TenantId = tenantA,
                RecipientMembershipId = membershipB,
                Type = WorkNotificationType.TaskAssigned,
                TaskId = task.Id,
                WorkspaceId = workspace.Id,
                ProjectId = project.Id,
                IsRead = false,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            };
            notificationB = forB.Id;
            sessionA.DbContext.WorkNotifications.Add(forB);
            await sessionA.DbContext.SaveChangesAsync();
            await sessionA.CommitAsync();
        }

        await using (var readA = await ScopedTenantSession.OpenAsync(_postgres.Services, userA, tenantA))
        {
            Assert.Empty(await readA.DbContext.WorkNotifications.ToListAsync());
        }

        await using (var readB = await ScopedTenantSession.OpenAsync(_postgres.Services, userB, tenantA))
        {
            var seen = Assert.Single(await readB.DbContext.WorkNotifications.ToListAsync());
            Assert.Equal(notificationB, seen.Id);
            Assert.Equal(taskId, seen.TaskId);
        }

        await using var foreign = await ScopedTenantSession.OpenAsync(_postgres.Services, userC, tenantC);
        Assert.Empty(await foreign.DbContext.WorkNotifications.ToListAsync());
        foreign.DbContext.WorkNotifications.Add(new WorkNotification
        {
            Id = Guid.NewGuid(),
            TenantId = tenantA,
            RecipientMembershipId = membershipB,
            Type = WorkNotificationType.TaskAssigned,
            IsRead = false,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => foreign.DbContext.SaveChangesAsync());
        _ = membershipA;
    }

    [SkippableFact]
    public async Task Comments_activity_and_read_state_are_tenant_isolated()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var factory = _postgres.Services.GetRequiredService<TestDataFactory>();
        var (userA, tenantA) = await factory.CreateUserWithTenantAsync("HistA");
        var (userB, tenantB) = await factory.CreateUserWithTenantAsync("HistB");
        var membershipA = await factory.GetMembershipIdAsync(userA, tenantA);
        var membershipB = await factory.GetMembershipIdAsync(userB, tenantB);

        Guid taskA;
        Guid commentA;
        Guid activityA;
        await using (var sessionA = await ScopedTenantSession.OpenAsync(_postgres.Services, userA, tenantA))
        {
            var workspace = new Workspace { Id = Guid.NewGuid(), TenantId = tenantA, Name = "HA", CreatedAtUtc = DateTimeOffset.UtcNow };
            sessionA.DbContext.Workspaces.Add(workspace);
            var project = new Project
            {
                Id = Guid.NewGuid(),
                TenantId = tenantA,
                WorkspaceId = workspace.Id,
                Name = "HP",
                CreatedAtUtc = DateTimeOffset.UtcNow,
            };
            sessionA.DbContext.Projects.Add(project);
            var task = new WorkTask
            {
                Id = Guid.NewGuid(),
                TenantId = tenantA,
                WorkspaceId = workspace.Id,
                ProjectId = project.Id,
                Title = "history-A",
                Status = WorkTaskStatus.Open,
                Priority = WorkTaskPriority.Normal,
                CreatedByMembershipId = membershipA,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            };
            taskA = task.Id;
            sessionA.DbContext.WorkTasks.Add(task);
            var comment = new WorkTaskComment
            {
                Id = Guid.NewGuid(),
                TenantId = tenantA,
                TaskId = task.Id,
                AuthorMembershipId = membershipA,
                Body = "tenant A note",
                CreatedAtUtc = DateTimeOffset.UtcNow,
            };
            commentA = comment.Id;
            sessionA.DbContext.WorkTaskComments.Add(comment);
            var activity = new WorkTaskActivity
            {
                Id = Guid.NewGuid(),
                TenantId = tenantA,
                TaskId = task.Id,
                ActorMembershipId = membershipA,
                EventType = WorkTaskActivityType.TaskCreated,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            };
            activityA = activity.Id;
            sessionA.DbContext.WorkTaskActivities.Add(activity);
            sessionA.DbContext.WorkTaskReadStates.Add(new WorkTaskReadState
            {
                TenantId = tenantA,
                TaskId = task.Id,
                MembershipId = membershipA,
                LastViewedAtUtc = DateTimeOffset.UtcNow,
            });
            await sessionA.DbContext.SaveChangesAsync();
            await sessionA.CommitAsync();
        }

        await using (var sessionB = await ScopedTenantSession.OpenAsync(_postgres.Services, userB, tenantB))
        {
            Assert.Empty(await sessionB.DbContext.WorkTaskComments.ToListAsync());
            Assert.Empty(await sessionB.DbContext.WorkTaskActivities.ToListAsync());
            Assert.Empty(await sessionB.DbContext.WorkTaskReadStates.ToListAsync());
            sessionB.DbContext.WorkTaskComments.Add(new WorkTaskComment
            {
                Id = Guid.NewGuid(),
                TenantId = tenantA,
                TaskId = taskA,
                AuthorMembershipId = membershipA,
                Body = "stolen",
                CreatedAtUtc = DateTimeOffset.UtcNow,
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => sessionB.DbContext.SaveChangesAsync());
        }

        await using var readA = await ScopedTenantSession.OpenAsync(_postgres.Services, userA, tenantA);
        Assert.Equal(commentA, Assert.Single(await readA.DbContext.WorkTaskComments.ToListAsync()).Id);
        Assert.Equal(activityA, Assert.Single(await readA.DbContext.WorkTaskActivities.ToListAsync()).Id);
        Assert.Equal(membershipA, Assert.Single(await readA.DbContext.WorkTaskReadStates.ToListAsync()).MembershipId);
        _ = membershipB;
    }
}
