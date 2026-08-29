using Microsoft.EntityFrameworkCore;
using PTS.Host.TenantAccess;
using PTS.Modules.Identity;
using PTS.Modules.Tenancy;
using PTS.Modules.WorkManagement;
using PTS.SharedKernel.Identity;

namespace PTS.Host.Http;

public static class WorkManagementEndpoints
{
    public static IEndpointRouteBuilder MapWorkManagementEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var tenant = endpoints.MapGroup("/tenants/{tenantId:guid}").RequireAuthorization();

        tenant.MapPost("/workspaces", CreateWorkspaceAsync);
        tenant.MapGet("/workspaces", ListWorkspacesAsync);
        tenant.MapGet("/workspaces/{workspaceId:guid}", GetWorkspaceAsync);
        tenant.MapPost("/workspaces/{workspaceId:guid}/projects", CreateProjectAsync);
        tenant.MapGet("/workspaces/{workspaceId:guid}/projects", ListProjectsAsync);
        tenant.MapGet("/members", ListMembersAsync);
        tenant.MapGet("/members/{membershipId:guid}/workspace-access", ListWorkspaceAccessAsync);
        tenant.MapPut("/members/{membershipId:guid}/workspace-access/{workspaceId:guid}", SetWorkspaceAccessAsync);
        tenant.MapDelete("/members/{membershipId:guid}/workspace-access/{workspaceId:guid}", RemoveWorkspaceAccessAsync);

        return endpoints;
    }

    private static async Task<IResult> CreateWorkspaceAsync(
        Guid tenantId,
        CreateWorkspaceRequest request,
        ICurrentUser currentUser,
        ITenantRlsSessionFactory sessions,
        WorkspaceAuthorizationService authorization,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.BadRequest(new { error = "invalid_workspace" });
        }

        try
        {
            await using var session = await sessions.OpenAsync(tenantId, cancellationToken);
            if (!authorization.CanCreateWorkspace(session.HasImplicitFullResourceAccess))
            {
                return Results.Json(
                    new { error = "workspace_create_forbidden" },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var workspace = new Workspace
            {
                Id = Guid.NewGuid(),
                TenantId = session.TenantId,
                Name = request.Name.Trim(),
                CreatedAtUtc = DateTimeOffset.UtcNow,
            };
            session.DbContext.Workspaces.Add(workspace);
            await session.DbContext.SaveChangesAsync(cancellationToken);
            await session.CommitAsync(cancellationToken);

            return Results.Created(
                $"/tenants/{session.TenantId}/workspaces/{workspace.Id}",
                new WorkspaceResponse(
                    workspace.Id,
                    workspace.TenantId,
                    workspace.Name,
                    WorkspaceAccessLevel.Edit.ToString()));
        }
        catch (AuthenticationRequiredException)
        {
            return Results.Unauthorized();
        }
        catch (UnknownAuthenticatedUserException)
        {
            return Results.Unauthorized();
        }
        catch (TenantAccessDeniedException)
        {
            return Results.Json(new { error = "tenant_access_denied" }, statusCode: StatusCodes.Status403Forbidden);
        }
    }

    private static async Task<IResult> ListWorkspacesAsync(
        Guid tenantId,
        ICurrentUser currentUser,
        ITenantRlsSessionFactory sessions,
        WorkspaceAuthorizationService authorization,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        try
        {
            await using var session = await sessions.OpenAsync(tenantId, cancellationToken);
            List<WorkspaceResponse> workspaces;
            if (session.HasImplicitFullResourceAccess)
            {
                workspaces = await session.DbContext.Workspaces
                    .AsNoTracking()
                    .OrderBy(workspace => workspace.Name)
                    .Select(workspace => new WorkspaceResponse(
                        workspace.Id,
                        workspace.TenantId,
                        workspace.Name,
                        nameof(WorkspaceAccessLevel.Edit)))
                    .ToListAsync(cancellationToken);
            }
            else
            {
                var rows = await (
                    from workspace in session.DbContext.Workspaces.AsNoTracking()
                    join access in session.DbContext.WorkspaceAccess.AsNoTracking()
                        on new { workspace.TenantId, WorkspaceId = workspace.Id }
                        equals new { access.TenantId, access.WorkspaceId }
                    where access.MembershipId == session.MembershipId
                    orderby workspace.Name
                    select new { Workspace = workspace, access.AccessLevel })
                    .ToListAsync(cancellationToken);

                workspaces = rows
                    .Where(row => authorization.CanViewWorkspace(false, row.AccessLevel))
                    .Select(row => new WorkspaceResponse(
                        row.Workspace.Id,
                        row.Workspace.TenantId,
                        row.Workspace.Name,
                        row.AccessLevel.ToString()))
                    .ToList();
            }

            await session.CommitAsync(cancellationToken);
            return Results.Ok(workspaces);
        }
        catch (AuthenticationRequiredException)
        {
            return Results.Unauthorized();
        }
        catch (UnknownAuthenticatedUserException)
        {
            return Results.Unauthorized();
        }
        catch (TenantAccessDeniedException)
        {
            return Results.Json(new { error = "tenant_access_denied" }, statusCode: StatusCodes.Status403Forbidden);
        }
    }

    private static async Task<IResult> GetWorkspaceAsync(
        Guid tenantId,
        Guid workspaceId,
        ICurrentUser currentUser,
        ITenantRlsSessionFactory sessions,
        WorkspaceAuthorizationService authorization,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        try
        {
            await using var session = await sessions.OpenAsync(tenantId, cancellationToken);
            var workspace = await session.DbContext.Workspaces
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == workspaceId, cancellationToken);
            if (workspace is null)
            {
                return Results.NotFound(new { error = "workspace_not_found" });
            }

            var explicitAccess = session.HasImplicitFullResourceAccess
                ? null
                : await session.DbContext.WorkspaceAccess
                    .AsNoTracking()
                    .Where(access =>
                        access.MembershipId == session.MembershipId &&
                        access.WorkspaceId == workspaceId)
                    .Select(access => (WorkspaceAccessLevel?)access.AccessLevel)
                    .FirstOrDefaultAsync(cancellationToken);

            if (!authorization.CanViewWorkspace(session.HasImplicitFullResourceAccess, explicitAccess))
            {
                return Results.NotFound(new { error = "workspace_not_found" });
            }

            await session.CommitAsync(cancellationToken);
            return Results.Ok(new WorkspaceResponse(
                workspace.Id,
                workspace.TenantId,
                workspace.Name,
                session.HasImplicitFullResourceAccess
                    ? nameof(WorkspaceAccessLevel.Edit)
                    : explicitAccess!.Value.ToString()));
        }
        catch (AuthenticationRequiredException)
        {
            return Results.Unauthorized();
        }
        catch (UnknownAuthenticatedUserException)
        {
            return Results.Unauthorized();
        }
        catch (TenantAccessDeniedException)
        {
            return Results.Json(new { error = "tenant_access_denied" }, statusCode: StatusCodes.Status403Forbidden);
        }
    }

    private static async Task<IResult> CreateProjectAsync(
        Guid tenantId,
        Guid workspaceId,
        CreateProjectRequest request,
        ICurrentUser currentUser,
        ITenantRlsSessionFactory sessions,
        WorkspaceAuthorizationService authorization,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.BadRequest(new { error = "invalid_project" });
        }

        try
        {
            await using var session = await sessions.OpenAsync(tenantId, cancellationToken);
            var workspace = await session.DbContext.Workspaces
                .AsNoTracking()
                .FirstOrDefaultAsync(w => w.Id == workspaceId, cancellationToken);
            if (workspace is null)
            {
                return Results.NotFound(new { error = "workspace_not_found" });
            }

            var explicitAccess = await GetExplicitAccessAsync(session, workspaceId, cancellationToken);
            if (!authorization.CanViewProject(session.HasImplicitFullResourceAccess, explicitAccess))
            {
                return Results.NotFound(new { error = "workspace_not_found" });
            }

            if (!authorization.CanEditProject(session.HasImplicitFullResourceAccess, explicitAccess))
            {
                return Results.Json(
                    new { error = "workspace_edit_forbidden" },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var project = new Project
            {
                Id = Guid.NewGuid(),
                TenantId = session.TenantId,
                WorkspaceId = workspace.Id,
                Name = request.Name.Trim(),
                CreatedAtUtc = DateTimeOffset.UtcNow,
            };
            session.DbContext.Projects.Add(project);
            await session.DbContext.SaveChangesAsync(cancellationToken);
            await session.CommitAsync(cancellationToken);

            return Results.Created(
                $"/tenants/{session.TenantId}/workspaces/{workspace.Id}/projects/{project.Id}",
                new ProjectResponse(project.Id, project.TenantId, project.WorkspaceId, project.Name));
        }
        catch (AuthenticationRequiredException)
        {
            return Results.Unauthorized();
        }
        catch (UnknownAuthenticatedUserException)
        {
            return Results.Unauthorized();
        }
        catch (TenantAccessDeniedException)
        {
            return Results.Json(new { error = "tenant_access_denied" }, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (DbUpdateException)
        {
            return Results.BadRequest(new { error = "invalid_project_workspace" });
        }
    }

    private static async Task<IResult> ListProjectsAsync(
        Guid tenantId,
        Guid workspaceId,
        ICurrentUser currentUser,
        ITenantRlsSessionFactory sessions,
        WorkspaceAuthorizationService authorization,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        try
        {
            await using var session = await sessions.OpenAsync(tenantId, cancellationToken);
            var workspace = await session.DbContext.Workspaces
                .AsNoTracking()
                .FirstOrDefaultAsync(w => w.Id == workspaceId, cancellationToken);
            if (workspace is null)
            {
                return Results.NotFound(new { error = "workspace_not_found" });
            }

            var explicitAccess = await GetExplicitAccessAsync(session, workspaceId, cancellationToken);
            if (!authorization.CanViewProject(session.HasImplicitFullResourceAccess, explicitAccess))
            {
                return Results.NotFound(new { error = "workspace_not_found" });
            }

            var projects = await session.DbContext.Projects
                .AsNoTracking()
                .Where(p => p.WorkspaceId == workspaceId)
                .ToListAsync(cancellationToken);
            await session.CommitAsync(cancellationToken);
            return Results.Ok(projects.Select(p => new ProjectResponse(p.Id, p.TenantId, p.WorkspaceId, p.Name)));
        }
        catch (AuthenticationRequiredException)
        {
            return Results.Unauthorized();
        }
        catch (UnknownAuthenticatedUserException)
        {
            return Results.Unauthorized();
        }
        catch (TenantAccessDeniedException)
        {
            return Results.Json(new { error = "tenant_access_denied" }, statusCode: StatusCodes.Status403Forbidden);
        }
    }

    private static async Task<IResult> ListMembersAsync(
        Guid tenantId,
        ICurrentUser currentUser,
        ITenantRlsSessionFactory sessions,
        WorkspaceAuthorizationService authorization,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        try
        {
            await using var session = await sessions.OpenAsync(tenantId, cancellationToken);
            if (!authorization.CanManageAccess(session.HasImplicitFullResourceAccess))
            {
                return Results.Json(
                    new { error = "workspace_access_manage_forbidden" },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var members = await (
                from membership in session.DbContext.Memberships.AsNoTracking()
                join user in session.DbContext.Users.AsNoTracking()
                    on membership.UserId equals user.Id
                orderby user.DisplayName, user.Email
                select new TenantMemberResponse(
                    membership.Id,
                    membership.UserId,
                    user.DisplayName,
                    user.Email,
                    membership.Role.ToString(),
                    membership.Status.ToString()))
                .ToListAsync(cancellationToken);

            await session.CommitAsync(cancellationToken);
            return Results.Ok(members);
        }
        catch (AuthenticationRequiredException)
        {
            return Results.Unauthorized();
        }
        catch (UnknownAuthenticatedUserException)
        {
            return Results.Unauthorized();
        }
        catch (TenantAccessDeniedException)
        {
            return Results.Json(new { error = "tenant_access_denied" }, statusCode: StatusCodes.Status403Forbidden);
        }
    }

    private static async Task<IResult> ListWorkspaceAccessAsync(
        Guid tenantId,
        Guid membershipId,
        ICurrentUser currentUser,
        ITenantRlsSessionFactory sessions,
        WorkspaceAuthorizationService authorization,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        try
        {
            await using var session = await sessions.OpenAsync(tenantId, cancellationToken);
            if (!authorization.CanManageAccess(session.HasImplicitFullResourceAccess))
            {
                return Results.Json(
                    new { error = "workspace_access_manage_forbidden" },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var membershipExists = await session.DbContext.Memberships
                .AsNoTracking()
                .AnyAsync(membership => membership.Id == membershipId, cancellationToken);
            if (!membershipExists)
            {
                return Results.NotFound(new { error = "membership_not_found" });
            }

            var access = await session.DbContext.WorkspaceAccess
                .AsNoTracking()
                .Where(item => item.MembershipId == membershipId)
                .OrderBy(item => item.WorkspaceId)
                .Select(item => new WorkspaceAccessResponse(
                    item.MembershipId,
                    item.WorkspaceId,
                    item.AccessLevel.ToString()))
                .ToListAsync(cancellationToken);

            await session.CommitAsync(cancellationToken);
            return Results.Ok(access);
        }
        catch (AuthenticationRequiredException)
        {
            return Results.Unauthorized();
        }
        catch (UnknownAuthenticatedUserException)
        {
            return Results.Unauthorized();
        }
        catch (TenantAccessDeniedException)
        {
            return Results.Json(new { error = "tenant_access_denied" }, statusCode: StatusCodes.Status403Forbidden);
        }
    }

    private static async Task<IResult> SetWorkspaceAccessAsync(
        Guid tenantId,
        Guid membershipId,
        Guid workspaceId,
        SetWorkspaceAccessRequest request,
        ICurrentUser currentUser,
        ITenantRlsSessionFactory sessions,
        WorkspaceAuthorizationService authorization,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        if (!Enum.TryParse<WorkspaceAccessLevel>(request.AccessLevel, true, out var accessLevel))
        {
            return Results.BadRequest(new { error = "invalid_workspace_access_level" });
        }

        try
        {
            await using var session = await sessions.OpenAsync(tenantId, cancellationToken);
            if (!authorization.CanManageAccess(session.HasImplicitFullResourceAccess))
            {
                return Results.Json(
                    new { error = "workspace_access_manage_forbidden" },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var target = await session.DbContext.Memberships
                .AsNoTracking()
                .FirstOrDefaultAsync(membership => membership.Id == membershipId, cancellationToken);
            if (target is null)
            {
                return Results.NotFound(new { error = "membership_not_found" });
            }

            if (target.Role != MembershipRole.Member || target.Status != MembershipStatus.Active)
            {
                return Results.BadRequest(new { error = "workspace_access_requires_active_member" });
            }

            var workspaceExists = await session.DbContext.Workspaces
                .AsNoTracking()
                .AnyAsync(workspace => workspace.Id == workspaceId, cancellationToken);
            if (!workspaceExists)
            {
                return Results.NotFound(new { error = "workspace_not_found" });
            }

            var now = DateTimeOffset.UtcNow;
            var existing = await session.DbContext.WorkspaceAccess
                .FirstOrDefaultAsync(
                    item => item.MembershipId == membershipId && item.WorkspaceId == workspaceId,
                    cancellationToken);
            if (existing is null)
            {
                session.DbContext.WorkspaceAccess.Add(new WorkspaceAccess
                {
                    Id = Guid.NewGuid(),
                    TenantId = session.TenantId,
                    MembershipId = membershipId,
                    WorkspaceId = workspaceId,
                    AccessLevel = accessLevel,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                });
            }
            else
            {
                existing.AccessLevel = accessLevel;
                existing.UpdatedAtUtc = now;
            }

            await session.DbContext.SaveChangesAsync(cancellationToken);
            await session.CommitAsync(cancellationToken);
            return Results.Ok(new WorkspaceAccessResponse(membershipId, workspaceId, accessLevel.ToString()));
        }
        catch (AuthenticationRequiredException)
        {
            return Results.Unauthorized();
        }
        catch (UnknownAuthenticatedUserException)
        {
            return Results.Unauthorized();
        }
        catch (TenantAccessDeniedException)
        {
            return Results.Json(new { error = "tenant_access_denied" }, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (DbUpdateException)
        {
            return Results.BadRequest(new { error = "invalid_workspace_access_relationship" });
        }
    }

    private static async Task<IResult> RemoveWorkspaceAccessAsync(
        Guid tenantId,
        Guid membershipId,
        Guid workspaceId,
        ICurrentUser currentUser,
        ITenantRlsSessionFactory sessions,
        WorkspaceAuthorizationService authorization,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        try
        {
            await using var session = await sessions.OpenAsync(tenantId, cancellationToken);
            if (!authorization.CanManageAccess(session.HasImplicitFullResourceAccess))
            {
                return Results.Json(
                    new { error = "workspace_access_manage_forbidden" },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var existing = await session.DbContext.WorkspaceAccess
                .FirstOrDefaultAsync(
                    item => item.MembershipId == membershipId && item.WorkspaceId == workspaceId,
                    cancellationToken);
            if (existing is not null)
            {
                session.DbContext.WorkspaceAccess.Remove(existing);
                await session.DbContext.SaveChangesAsync(cancellationToken);
            }

            await session.CommitAsync(cancellationToken);
            return Results.NoContent();
        }
        catch (AuthenticationRequiredException)
        {
            return Results.Unauthorized();
        }
        catch (UnknownAuthenticatedUserException)
        {
            return Results.Unauthorized();
        }
        catch (TenantAccessDeniedException)
        {
            return Results.Json(new { error = "tenant_access_denied" }, statusCode: StatusCodes.Status403Forbidden);
        }
    }

    private static Task<WorkspaceAccessLevel?> GetExplicitAccessAsync(
        TenantRlsSession session,
        Guid workspaceId,
        CancellationToken cancellationToken)
    {
        if (session.HasImplicitFullResourceAccess)
        {
            return Task.FromResult<WorkspaceAccessLevel?>(null);
        }

        return session.DbContext.WorkspaceAccess
            .AsNoTracking()
            .Where(access =>
                access.MembershipId == session.MembershipId &&
                access.WorkspaceId == workspaceId)
            .Select(access => (WorkspaceAccessLevel?)access.AccessLevel)
            .FirstOrDefaultAsync(cancellationToken);
    }
}

public sealed record CreateWorkspaceRequest(string Name, Guid? TenantId = null);

public sealed record CreateProjectRequest(string Name, Guid? TenantId = null);

public sealed record SetWorkspaceAccessRequest(string AccessLevel);

public sealed record WorkspaceResponse(Guid WorkspaceId, Guid TenantId, string Name, string AccessLevel);

public sealed record ProjectResponse(Guid ProjectId, Guid TenantId, Guid WorkspaceId, string Name);

public sealed record TenantMemberResponse(
    Guid MembershipId,
    Guid UserId,
    string DisplayName,
    string Email,
    string Role,
    string Status);

public sealed record WorkspaceAccessResponse(Guid MembershipId, Guid WorkspaceId, string AccessLevel);
