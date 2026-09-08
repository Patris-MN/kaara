using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PTS.Host.TenantAccess;
using PTS.Modules.Tenancy;
using PTS.Modules.WorkManagement;
using PTS.SharedKernel.Identity;

namespace PTS.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class WorkManagementRlsTests
{
    private readonly PostgresFixture _postgres;

    public WorkManagementRlsTests(PostgresFixture postgres)
    {
        _postgres = postgres;
    }

    [SkippableFact]
    public async Task Tenant_A_sees_only_its_workspaces_without_an_application_tenant_filter()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var factory = _postgres.Services.GetRequiredService<TestDataFactory>();
        var (userA, tenantA) = await factory.CreateUserWithTenantAsync("WmA");
        var (userB, tenantB) = await factory.CreateUserWithTenantAsync("WmB");

        await using (var sessionA = await ScopedTenantSession.OpenAsync(_postgres.Services, userA, tenantA))
        {
            sessionA.DbContext.Workspaces.Add(new Workspace
            {
                Id = Guid.NewGuid(),
                TenantId = tenantA,
                Name = "A1",
                CreatedAtUtc = DateTimeOffset.UtcNow,
            });
            sessionA.DbContext.Workspaces.Add(new Workspace
            {
                Id = Guid.NewGuid(),
                TenantId = tenantA,
                Name = "A2",
                CreatedAtUtc = DateTimeOffset.UtcNow,
            });
            await sessionA.DbContext.SaveChangesAsync();
            await sessionA.CommitAsync();
        }

        await using (var sessionB = await ScopedTenantSession.OpenAsync(_postgres.Services, userB, tenantB))
        {
            sessionB.DbContext.Workspaces.Add(new Workspace
            {
                Id = Guid.NewGuid(),
                TenantId = tenantB,
                Name = "B1",
                CreatedAtUtc = DateTimeOffset.UtcNow,
            });
            await sessionB.DbContext.SaveChangesAsync();
            await sessionB.CommitAsync();
        }

        await using var readA = await ScopedTenantSession.OpenAsync(_postgres.Services, userA, tenantA);
        var seen = await readA.DbContext.Workspaces.ToListAsync();
        Assert.Equal(2, seen.Count);
        Assert.All(seen, w => Assert.Equal(tenantA, w.TenantId));
        Assert.DoesNotContain(seen, w => w.Name == "B1");
    }

    [SkippableFact]
    public async Task Tenant_B_sees_only_its_projects_without_an_application_tenant_filter()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var factory = _postgres.Services.GetRequiredService<TestDataFactory>();
        var (userA, tenantA) = await factory.CreateUserWithTenantAsync("ProjA");
        var (userB, tenantB) = await factory.CreateUserWithTenantAsync("ProjB");

        Guid workspaceA;
        Guid workspaceB;
        await using (var sessionA = await ScopedTenantSession.OpenAsync(_postgres.Services, userA, tenantA))
        {
            var ws = new Workspace { Id = Guid.NewGuid(), TenantId = tenantA, Name = "WA", CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow };
            workspaceA = ws.Id;
            sessionA.DbContext.Workspaces.Add(ws);
            sessionA.DbContext.Projects.Add(TestProjectFactory.SeedProject(tenantA, ws.Id, "PA"));
            await sessionA.DbContext.SaveChangesAsync();
            await sessionA.CommitAsync();
        }

        await using (var sessionB = await ScopedTenantSession.OpenAsync(_postgres.Services, userB, tenantB))
        {
            var ws = new Workspace { Id = Guid.NewGuid(), TenantId = tenantB, Name = "WB", CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow };
            workspaceB = ws.Id;
            sessionB.DbContext.Workspaces.Add(ws);
            sessionB.DbContext.Projects.Add(TestProjectFactory.SeedProject(tenantB, ws.Id, "PB"));
            await sessionB.DbContext.SaveChangesAsync();
            await sessionB.CommitAsync();
        }

        await using var readA = await ScopedTenantSession.OpenAsync(_postgres.Services, userA, tenantA);
        var projectsA = await readA.DbContext.Projects.ToListAsync();
        Assert.Single(projectsA);
        Assert.Equal("PA", projectsA[0].Name);
        Assert.DoesNotContain(projectsA, p => p.Name == "PB");
        _ = workspaceA;
        _ = workspaceB;
    }

    [SkippableFact]
    public async Task Tenant_A_cannot_insert_a_workspace_for_tenant_B()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var factory = _postgres.Services.GetRequiredService<TestDataFactory>();
        var (userA, tenantA) = await factory.CreateUserWithTenantAsync("InsA");
        var (_, tenantB) = await factory.CreateUserWithTenantAsync("InsB");

        await using var session = await ScopedTenantSession.OpenAsync(_postgres.Services, userA, tenantA);
        session.DbContext.Workspaces.Add(new Workspace
        {
            Id = Guid.NewGuid(),
            TenantId = tenantB,
            Name = "stolen",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => session.DbContext.SaveChangesAsync());
    }

    [SkippableFact]
    public async Task Tenant_A_cannot_update_workspace_tenant_id_to_tenant_B()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var factory = _postgres.Services.GetRequiredService<TestDataFactory>();
        var (userA, tenantA) = await factory.CreateUserWithTenantAsync("UpA");
        var (_, tenantB) = await factory.CreateUserWithTenantAsync("UpB");

        Guid workspaceId;
        await using (var session = await ScopedTenantSession.OpenAsync(_postgres.Services, userA, tenantA))
        {
            var ws = new Workspace { Id = Guid.NewGuid(), TenantId = tenantA, Name = "move", CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow };
            workspaceId = ws.Id;
            session.DbContext.Workspaces.Add(ws);
            await session.DbContext.SaveChangesAsync();
            await session.CommitAsync();
        }

        await using var update = await ScopedTenantSession.OpenAsync(_postgres.Services, userA, tenantA);
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await update.DbContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE workspaces SET tenant_id = {tenantB} WHERE id = {workspaceId}"));
    }

    [SkippableFact]
    public async Task Project_cannot_reference_a_workspace_from_another_tenant()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var factory = _postgres.Services.GetRequiredService<TestDataFactory>();
        var (userA, tenantA) = await factory.CreateUserWithTenantAsync("FkA");
        var (userB, tenantB) = await factory.CreateUserWithTenantAsync("FkB");

        Guid workspaceB;
        await using (var sessionB = await ScopedTenantSession.OpenAsync(_postgres.Services, userB, tenantB))
        {
            var ws = new Workspace { Id = Guid.NewGuid(), TenantId = tenantB, Name = "B", CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow };
            workspaceB = ws.Id;
            sessionB.DbContext.Workspaces.Add(ws);
            await sessionB.DbContext.SaveChangesAsync();
            await sessionB.CommitAsync();
        }

        await using var sessionA = await ScopedTenantSession.OpenAsync(_postgres.Services, userA, tenantA);
        sessionA.DbContext.Projects.Add(TestProjectFactory.SeedProject(tenantA, workspaceB, "cross"));
        await Assert.ThrowsAsync<DbUpdateException>(() => sessionA.DbContext.SaveChangesAsync());
    }

    [SkippableFact]
    public async Task Alternating_tenants_do_not_leak_workspaces_across_pooled_sessions()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var factory = _postgres.Services.GetRequiredService<TestDataFactory>();
        var (userA, tenantA) = await factory.CreateUserWithTenantAsync("PoolWmA");
        var (userB, tenantB) = await factory.CreateUserWithTenantAsync("PoolWmB");

        await using (var sessionA = await ScopedTenantSession.OpenAsync(_postgres.Services, userA, tenantA))
        {
            sessionA.DbContext.Workspaces.Add(new Workspace
            {
                Id = Guid.NewGuid(),
                TenantId = tenantA,
                Name = "pool-A",
                CreatedAtUtc = DateTimeOffset.UtcNow,
            });
            await sessionA.DbContext.SaveChangesAsync();
            await sessionA.CommitAsync();
        }

        await using (var sessionB = await ScopedTenantSession.OpenAsync(_postgres.Services, userB, tenantB))
        {
            sessionB.DbContext.Workspaces.Add(new Workspace
            {
                Id = Guid.NewGuid(),
                TenantId = tenantB,
                Name = "pool-B",
                CreatedAtUtc = DateTimeOffset.UtcNow,
            });
            await sessionB.DbContext.SaveChangesAsync();
            await sessionB.CommitAsync();
        }

        for (var i = 0; i < 8; i++)
        {
            await using (var sessionA = await ScopedTenantSession.OpenAsync(_postgres.Services, userA, tenantA))
            {
                var names = await sessionA.DbContext.Workspaces.Select(w => w.Name).ToListAsync();
                Assert.Contains("pool-A", names);
                Assert.DoesNotContain("pool-B", names);
            }

            await using (var sessionB = await ScopedTenantSession.OpenAsync(_postgres.Services, userB, tenantB))
            {
                var names = await sessionB.DbContext.Workspaces.Select(w => w.Name).ToListAsync();
                Assert.Contains("pool-B", names);
                Assert.DoesNotContain("pool-A", names);
            }
        }
    }

    [SkippableFact]
    public async Task Workspace_access_rows_are_hidden_across_tenants_and_reject_cross_tenant_relationships()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var factory = _postgres.Services.GetRequiredService<TestDataFactory>();
        var (userA, tenantA) = await factory.CreateUserWithTenantAsync("AccA");
        var (userB, tenantB) = await factory.CreateUserWithTenantAsync("AccB");
        var membershipA = await factory.GetMembershipIdAsync(userA, tenantA);
        var membershipB = await factory.GetMembershipIdAsync(userB, tenantB);

        Guid workspaceA;
        Guid workspaceB;
        await using (var sessionA = await ScopedTenantSession.OpenAsync(_postgres.Services, userA, tenantA))
        {
            var workspace = new Workspace
            {
                Id = Guid.NewGuid(),
                TenantId = tenantA,
                Name = "access-A",
                CreatedAtUtc = DateTimeOffset.UtcNow,
            };
            workspaceA = workspace.Id;
            sessionA.DbContext.Workspaces.Add(workspace);
            await sessionA.DbContext.SaveChangesAsync();
            await sessionA.CommitAsync();
        }

        await using (var sessionB = await ScopedTenantSession.OpenAsync(_postgres.Services, userB, tenantB))
        {
            var workspace = new Workspace
            {
                Id = Guid.NewGuid(),
                TenantId = tenantB,
                Name = "access-B",
                CreatedAtUtc = DateTimeOffset.UtcNow,
            };
            workspaceB = workspace.Id;
            sessionB.DbContext.Workspaces.Add(workspace);
            await sessionB.DbContext.SaveChangesAsync();
            await sessionB.CommitAsync();
        }

        await using (var sessionA = await ScopedTenantSession.OpenAsync(_postgres.Services, userA, tenantA))
        {
            sessionA.DbContext.WorkspaceAccess.Add(new WorkspaceAccess
            {
                Id = Guid.NewGuid(),
                TenantId = tenantA,
                MembershipId = membershipA,
                WorkspaceId = workspaceA,
                AccessLevel = WorkspaceAccessLevel.View,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            });
            await sessionA.DbContext.SaveChangesAsync();
            await sessionA.CommitAsync();
        }

        await using (var sessionB = await ScopedTenantSession.OpenAsync(_postgres.Services, userB, tenantB))
        {
            sessionB.DbContext.WorkspaceAccess.Add(new WorkspaceAccess
            {
                Id = Guid.NewGuid(),
                TenantId = tenantB,
                MembershipId = membershipB,
                WorkspaceId = workspaceB,
                AccessLevel = WorkspaceAccessLevel.Edit,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            });
            await sessionB.DbContext.SaveChangesAsync();
            await sessionB.CommitAsync();
        }

        await using (var readA = await ScopedTenantSession.OpenAsync(_postgres.Services, userA, tenantA))
        {
            var seen = await readA.DbContext.WorkspaceAccess.ToListAsync();
            Assert.Single(seen);
            Assert.Equal(tenantA, seen[0].TenantId);
            Assert.Equal(workspaceA, seen[0].WorkspaceId);
            Assert.DoesNotContain(seen, row => row.WorkspaceId == workspaceB);
        }

        await using (var sessionA = await ScopedTenantSession.OpenAsync(_postgres.Services, userA, tenantA))
        {
            sessionA.DbContext.WorkspaceAccess.Add(new WorkspaceAccess
            {
                Id = Guid.NewGuid(),
                TenantId = tenantA,
                MembershipId = membershipA,
                WorkspaceId = workspaceB,
                AccessLevel = WorkspaceAccessLevel.View,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => sessionA.DbContext.SaveChangesAsync());
        }

        await using var foreignTenant = await ScopedTenantSession.OpenAsync(_postgres.Services, userA, tenantA);
        foreignTenant.DbContext.WorkspaceAccess.Add(new WorkspaceAccess
        {
            Id = Guid.NewGuid(),
            TenantId = tenantB,
            MembershipId = membershipB,
            WorkspaceId = workspaceB,
            AccessLevel = WorkspaceAccessLevel.View,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => foreignTenant.DbContext.SaveChangesAsync());
    }

    [SkippableFact]
    public async Task Tasks_are_hidden_across_tenants_and_reject_cross_hierarchy_relationships()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var factory = _postgres.Services.GetRequiredService<TestDataFactory>();
        var (userA, tenantA) = await factory.CreateUserWithTenantAsync("TaskA");
        var (userB, tenantB) = await factory.CreateUserWithTenantAsync("TaskB");
        var membershipA = await factory.GetMembershipIdAsync(userA, tenantA);
        var membershipB = await factory.GetMembershipIdAsync(userB, tenantB);

        Guid workspaceA;
        Guid workspaceA2;
        Guid projectA;
        Guid projectA2;
        Guid workspaceB;
        Guid projectB;
        await using (var sessionA = await ScopedTenantSession.OpenAsync(_postgres.Services, userA, tenantA))
        {
            var first = new Workspace { Id = Guid.NewGuid(), TenantId = tenantA, Name = "WA1", CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow };
            var second = new Workspace { Id = Guid.NewGuid(), TenantId = tenantA, Name = "WA2", CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow };
            workspaceA = first.Id;
            workspaceA2 = second.Id;
            sessionA.DbContext.Workspaces.AddRange(first, second);
            var project = TestProjectFactory.SeedProject(tenantA, first.Id, "PA1");
            var otherProject = TestProjectFactory.SeedProject(tenantA, second.Id, "PA2");
            projectA = project.Id;
            projectA2 = otherProject.Id;
            sessionA.DbContext.Projects.AddRange(project, otherProject);
            sessionA.DbContext.WorkTasks.Add(
                TestProjectFactory.SeedWorkTask(tenantA, first.Id, project.Id, membershipA, "task-A"));
            await sessionA.DbContext.SaveChangesAsync();
            await sessionA.CommitAsync();
        }

        await using (var sessionB = await ScopedTenantSession.OpenAsync(_postgres.Services, userB, tenantB))
        {
            var workspace = new Workspace { Id = Guid.NewGuid(), TenantId = tenantB, Name = "WB", CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow };
            workspaceB = workspace.Id;
            sessionB.DbContext.Workspaces.Add(workspace);
            var project = TestProjectFactory.SeedProject(tenantB, workspace.Id, "PB");
            projectB = project.Id;
            sessionB.DbContext.Projects.Add(project);
            sessionB.DbContext.WorkTasks.Add(
                TestProjectFactory.SeedWorkTask(tenantB, workspace.Id, project.Id, membershipB, "task-B", priority: WorkTaskPriority.Low));
            await sessionB.DbContext.SaveChangesAsync();
            await sessionB.CommitAsync();
        }

        await using (var readA = await ScopedTenantSession.OpenAsync(_postgres.Services, userA, tenantA))
        {
            var titles = await readA.DbContext.WorkTasks.Select(task => task.Title).ToListAsync();
            Assert.Contains("task-A", titles);
            Assert.DoesNotContain("task-B", titles);
        }

        await using (var sessionA = await ScopedTenantSession.OpenAsync(_postgres.Services, userA, tenantA))
        {
            sessionA.DbContext.WorkTasks.Add(
                TestProjectFactory.SeedWorkTask(tenantB, workspaceB, projectB, membershipA, "stolen", priority: WorkTaskPriority.High));
            await Assert.ThrowsAsync<DbUpdateException>(() => sessionA.DbContext.SaveChangesAsync());
        }

        await using (var sessionA = await ScopedTenantSession.OpenAsync(_postgres.Services, userA, tenantA))
        {
            sessionA.DbContext.WorkTasks.Add(
                TestProjectFactory.SeedWorkTask(tenantA, workspaceA, projectB, membershipA, "cross-tenant-project"));
            await Assert.ThrowsAsync<DbUpdateException>(() => sessionA.DbContext.SaveChangesAsync());
        }

        await using var mismatch = await ScopedTenantSession.OpenAsync(_postgres.Services, userA, tenantA);
        mismatch.DbContext.WorkTasks.Add(
            TestProjectFactory.SeedWorkTask(tenantA, workspaceA, projectA2, membershipA, "wrong-workspace"));
        await Assert.ThrowsAsync<DbUpdateException>(() => mismatch.DbContext.SaveChangesAsync());
        _ = projectA;
        _ = workspaceA2;
    }

    [SkippableFact]
    public async Task Alternating_tenants_do_not_leak_tasks_across_pooled_sessions()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var factory = _postgres.Services.GetRequiredService<TestDataFactory>();
        var (userA, tenantA) = await factory.CreateUserWithTenantAsync("PoolTaskA");
        var (userB, tenantB) = await factory.CreateUserWithTenantAsync("PoolTaskB");
        var membershipA = await factory.GetMembershipIdAsync(userA, tenantA);
        var membershipB = await factory.GetMembershipIdAsync(userB, tenantB);

        await using (var sessionA = await ScopedTenantSession.OpenAsync(_postgres.Services, userA, tenantA))
        {
            var workspace = new Workspace { Id = Guid.NewGuid(), TenantId = tenantA, Name = "pool-WA", CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow };
            sessionA.DbContext.Workspaces.Add(workspace);
            var project = TestProjectFactory.SeedProject(tenantA, workspace.Id, "pool-PA");
            sessionA.DbContext.Projects.Add(project);
            sessionA.DbContext.WorkTasks.Add(
                TestProjectFactory.SeedWorkTask(tenantA, workspace.Id, project.Id, membershipA, "pool-task-A"));
            await sessionA.DbContext.SaveChangesAsync();
            await sessionA.CommitAsync();
        }

        await using (var sessionB = await ScopedTenantSession.OpenAsync(_postgres.Services, userB, tenantB))
        {
            var workspace = new Workspace { Id = Guid.NewGuid(), TenantId = tenantB, Name = "pool-WB", CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow };
            sessionB.DbContext.Workspaces.Add(workspace);
            var project = TestProjectFactory.SeedProject(tenantB, workspace.Id, "pool-PB");
            sessionB.DbContext.Projects.Add(project);
            sessionB.DbContext.WorkTasks.Add(
                TestProjectFactory.SeedWorkTask(
                    tenantB,
                    workspace.Id,
                    project.Id,
                    membershipB,
                    "pool-task-B",
                    status: WorkTaskStatus.Closed,
                    priority: WorkTaskPriority.High));
            await sessionB.DbContext.SaveChangesAsync();
            await sessionB.CommitAsync();
        }

        for (var i = 0; i < 8; i++)
        {
            await using (var sessionA = await ScopedTenantSession.OpenAsync(_postgres.Services, userA, tenantA))
            {
                var titles = await sessionA.DbContext.WorkTasks.Select(task => task.Title).ToListAsync();
                Assert.Contains("pool-task-A", titles);
                Assert.DoesNotContain("pool-task-B", titles);
            }

            await using (var sessionB = await ScopedTenantSession.OpenAsync(_postgres.Services, userB, tenantB))
            {
                var titles = await sessionB.DbContext.WorkTasks.Select(task => task.Title).ToListAsync();
                Assert.Contains("pool-task-B", titles);
                Assert.DoesNotContain("pool-task-A", titles);
            }
        }
    }
}
