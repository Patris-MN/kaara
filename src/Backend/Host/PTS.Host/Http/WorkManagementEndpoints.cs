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
        tenant.MapPut("/workspaces/{workspaceId:guid}", UpdateWorkspaceAsync);
        tenant.MapPost("/workspaces/{workspaceId:guid}/projects", CreateProjectAsync);
        tenant.MapGet("/workspaces/{workspaceId:guid}/projects", ListProjectsAsync);
        tenant.MapPatch("/workspaces/{workspaceId:guid}/projects/{projectId:guid}", UpdateProjectAsync);
        tenant.MapDelete("/workspaces/{workspaceId:guid}/projects/{projectId:guid}", DeleteProjectAsync);
        tenant.MapGet("/members", ListMembersAsync);
        tenant.MapGet("/members/{membershipId:guid}/workspace-access", ListWorkspaceAccessAsync);
        tenant.MapPut("/members/{membershipId:guid}/workspace-access", ReplaceWorkspaceAccessAsync);
        tenant.MapPut("/members/{membershipId:guid}/workspace-access/{workspaceId:guid}", SetWorkspaceAccessAsync);
        tenant.MapDelete("/members/{membershipId:guid}/workspace-access/{workspaceId:guid}", RemoveWorkspaceAccessAsync);
        tenant.MapGet("/workspaces/{workspaceId:guid}/member-access", ListWorkspaceMemberAccessAsync);
        tenant.MapPost("/workspaces/{workspaceId:guid}/member-access", GrantWorkspaceMemberAccessAsync);

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

        if (request.Description is not null && request.Description.Length > 500)
        {
            return Results.BadRequest(new { error = "invalid_workspace_description" });
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

            var now = DateTimeOffset.UtcNow;
            var (displayName, normalizedName) = ResourceNameConflicts.ParseName(request.Name);
            var workspace = new Workspace
            {
                Id = Guid.NewGuid(),
                TenantId = session.TenantId,
                Name = displayName,
                NameNormalized = normalizedName,
                Description = NormalizeDescription(request.Description),
                StartDate = request.StartDate,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };
            session.DbContext.Workspaces.Add(workspace);
            try
            {
                await session.DbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (ResourceNameConflicts.IsUniqueViolation(ex))
            {
                var existingName = await ResourceNameConflicts.FindWorkspaceDisplayNameAsync(
                    session.DbContext,
                    session.TenantId,
                    normalizedName,
                    excludeWorkspaceId: null,
                    cancellationToken);
                return Results.Conflict(new { error = "workspace_name_conflict", existingName });
            }

            await session.CommitAsync(cancellationToken);

            return Results.Created(
                $"/tenants/{session.TenantId}/workspaces/{workspace.Id}",
                MapWorkspace(workspace, WorkspaceAccessLevel.Edit.ToString(), canManage: true));
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
                var rows = await session.DbContext.Workspaces
                    .AsNoTracking()
                    .Where(workspace => workspace.TenantId == session.TenantId)
                    .OrderByDescending(workspace => workspace.UpdatedAtUtc ?? workspace.CreatedAtUtc)
                    .ThenBy(workspace => workspace.Name)
                    .ToListAsync(cancellationToken);
                workspaces = rows
                    .Select(workspace => MapWorkspace(
                        workspace,
                        nameof(WorkspaceAccessLevel.Edit),
                        canManage: true))
                    .ToList();
            }
            else
            {
                var rows = await (
                    from workspace in session.DbContext.Workspaces.AsNoTracking()
                    join access in session.DbContext.WorkspaceAccess.AsNoTracking()
                        on new { workspace.TenantId, WorkspaceId = workspace.Id }
                        equals new { access.TenantId, access.WorkspaceId }
                    where workspace.TenantId == session.TenantId
                      && access.MembershipId == session.MembershipId
                    orderby (workspace.UpdatedAtUtc ?? workspace.CreatedAtUtc) descending, workspace.Name
                    select new { Workspace = workspace, access.AccessLevel })
                    .ToListAsync(cancellationToken);

                workspaces = rows
                    .Where(row => authorization.CanViewWorkspace(false, row.AccessLevel))
                    .Select(row => MapWorkspace(
                        row.Workspace,
                        row.AccessLevel.ToString(),
                        authorization.CanEditWorkspace(false, row.AccessLevel)))
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

            var accessLevel = session.HasImplicitFullResourceAccess
                ? nameof(WorkspaceAccessLevel.Edit)
                : explicitAccess!.Value.ToString();
            var canManage = authorization.CanEditWorkspace(session.HasImplicitFullResourceAccess, explicitAccess);

            await session.CommitAsync(cancellationToken);
            return Results.Ok(MapWorkspace(workspace, accessLevel, canManage));
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

    private static async Task<IResult> UpdateWorkspaceAsync(
        Guid tenantId,
        Guid workspaceId,
        UpdateWorkspaceRequest request,
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

        if (request.Description is not null && request.Description.Length > 500)
        {
            return Results.BadRequest(new { error = "invalid_workspace_description" });
        }

        try
        {
            await using var session = await sessions.OpenAsync(tenantId, cancellationToken);
            var workspace = await session.DbContext.Workspaces
                .FirstOrDefaultAsync(item => item.Id == workspaceId && item.TenantId == session.TenantId, cancellationToken);
            if (workspace is null)
            {
                return Results.NotFound(new { error = "workspace_not_found" });
            }

            var explicitAccess = await GetExplicitAccessAsync(session, workspaceId, cancellationToken);
            if (!authorization.CanEditWorkspace(session.HasImplicitFullResourceAccess, explicitAccess))
            {
                return Results.Json(
                    new { error = "workspace_edit_forbidden" },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var (displayName, normalizedName) = ResourceNameConflicts.ParseName(request.Name);
            ResourceNameConflicts.ApplyWorkspaceName(workspace, displayName);
            workspace.Description = NormalizeDescription(request.Description);
            workspace.StartDate = request.StartDate;
            workspace.UpdatedAtUtc = DateTimeOffset.UtcNow;
            try
            {
                await session.DbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (ResourceNameConflicts.IsUniqueViolation(ex))
            {
                var existingName = await ResourceNameConflicts.FindWorkspaceDisplayNameAsync(
                    session.DbContext,
                    session.TenantId,
                    normalizedName,
                    excludeWorkspaceId: workspaceId,
                    cancellationToken);
                return Results.Conflict(new { error = "workspace_name_conflict", existingName });
            }

            await session.CommitAsync(cancellationToken);

            var accessLevel = session.HasImplicitFullResourceAccess
                ? nameof(WorkspaceAccessLevel.Edit)
                : explicitAccess!.Value.ToString();

            return Results.Ok(MapWorkspace(workspace, accessLevel, canManage: true));
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

        if (!ProjectAccentRules.TryNormalize(request.AccentToken, out _))
        {
            return Results.BadRequest(new { error = "invalid_project_accent" });
        }

        if (request.Description is { Length: > ProjectConfiguration.DescriptionMaxLength })
        {
            return Results.BadRequest(new { error = "invalid_project_description" });
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

            var (displayName, normalizedName) = ResourceNameConflicts.ParseName(request.Name);
            var project = new Project
            {
                Id = Guid.NewGuid(),
                TenantId = session.TenantId,
                WorkspaceId = workspace.Id,
                Name = displayName,
                NameNormalized = normalizedName,
                Description = NormalizeProjectDescription(request.Description),
                AccentToken = ProjectAccentRules.ResolveOrDefault(request.AccentToken),
                CreatedAtUtc = DateTimeOffset.UtcNow,
            };
            session.DbContext.Projects.Add(project);
            try
            {
                await session.DbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (ResourceNameConflicts.IsUniqueViolation(ex))
            {
                var existingName = await ResourceNameConflicts.FindProjectDisplayNameAsync(
                    session.DbContext,
                    session.TenantId,
                    workspace.Id,
                    normalizedName,
                    excludeProjectId: null,
                    cancellationToken);
                return Results.Conflict(new { error = "project_name_conflict", existingName });
            }

            await session.CommitAsync(cancellationToken);

            return Results.Created(
                $"/tenants/{session.TenantId}/workspaces/{workspace.Id}/projects/{project.Id}",
                MapProject(project, openTaskCount: 0, taskCount: 0));
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
                .OrderBy(p => p.Name)
                .ToListAsync(cancellationToken);

            var projectIds = projects.Select(p => p.Id).ToList();
            var (openCounts, taskCounts) = await LoadProjectTaskCountsAsync(
                session,
                projectIds,
                cancellationToken);

            await session.CommitAsync(cancellationToken);
            return Results.Ok(projects.Select(project =>
                MapProject(
                    project,
                    openCounts.GetValueOrDefault(project.Id),
                    taskCounts.GetValueOrDefault(project.Id))));
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

    private static async Task<IResult> UpdateProjectAsync(
        Guid tenantId,
        Guid workspaceId,
        Guid projectId,
        UpdateProjectRequest request,
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

        if (!ProjectAccentRules.TryNormalize(request.AccentToken, out _))
        {
            return Results.BadRequest(new { error = "invalid_project_accent" });
        }

        if (request.Description is { Length: > ProjectConfiguration.DescriptionMaxLength })
        {
            return Results.BadRequest(new { error = "invalid_project_description" });
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

            if (!authorization.CanManageProjectMetadata(session.HasImplicitFullResourceAccess))
            {
                return Results.Json(
                    new { error = "project_metadata_edit_forbidden" },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var project = await session.DbContext.Projects
                .FirstOrDefaultAsync(
                    p => p.Id == projectId && p.WorkspaceId == workspaceId,
                    cancellationToken);
            if (project is null)
            {
                return Results.NotFound(new { error = "project_not_found" });
            }

            var (displayName, normalizedName) = ResourceNameConflicts.ParseName(request.Name);
            ResourceNameConflicts.ApplyProjectName(project, displayName);
            project.Description = NormalizeProjectDescription(request.Description);
            project.AccentToken = ProjectAccentRules.ResolveOrDefault(request.AccentToken);

            try
            {
                await session.DbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (ResourceNameConflicts.IsUniqueViolation(ex))
            {
                var existingName = await ResourceNameConflicts.FindProjectDisplayNameAsync(
                    session.DbContext,
                    session.TenantId,
                    workspace.Id,
                    normalizedName,
                    excludeProjectId: project.Id,
                    cancellationToken);
                return Results.Conflict(new { error = "project_name_conflict", existingName });
            }

            var (openCounts, taskCounts) = await LoadProjectTaskCountsAsync(
                session,
                [project.Id],
                cancellationToken);

            await session.CommitAsync(cancellationToken);
            return Results.Ok(MapProject(
                project,
                openCounts.GetValueOrDefault(project.Id),
                taskCounts.GetValueOrDefault(project.Id)));
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

    private static async Task<IResult> DeleteProjectAsync(
        Guid tenantId,
        Guid workspaceId,
        Guid projectId,
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

            if (!authorization.CanDeleteProject(session.HasImplicitFullResourceAccess))
            {
                return Results.Json(
                    new { error = "project_delete_forbidden" },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var project = await session.DbContext.Projects
                .FirstOrDefaultAsync(
                    p => p.Id == projectId && p.WorkspaceId == workspaceId,
                    cancellationToken);
            if (project is null)
            {
                return Results.NotFound(new { error = "project_not_found" });
            }

            var taskCount = await session.DbContext.WorkTasks
                .AsNoTracking()
                .CountAsync(
                    task => task.TenantId == session.TenantId && task.ProjectId == project.Id,
                    cancellationToken);
            if (taskCount > 0)
            {
                return Results.Conflict(new { error = "project_has_tasks", taskCount });
            }

            session.DbContext.Projects.Remove(project);
            await session.DbContext.SaveChangesAsync(cancellationToken);
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
                where membership.TenantId == session.TenantId
                orderby user.DisplayName, user.Email
                select new
                {
                    membership.Id,
                    membership.UserId,
                    user.DisplayName,
                    user.Email,
                    membership.Role,
                    membership.Status,
                    membership.CreatedAtUtc,
                    WorkspaceAccessCount = session.DbContext.WorkspaceAccess.Count(
                        item => item.MembershipId == membership.Id),
                })
                .ToListAsync(cancellationToken);

            var assigneeStats = await session.DbContext.WorkTasks
                .AsNoTracking()
                .Where(task => task.TenantId == session.TenantId && task.AssignedMembershipId != null)
                .GroupBy(task => task.AssignedMembershipId!.Value)
                .Select(group => new
                {
                    MembershipId = group.Key,
                    TotalAssignedTaskCount = group.Count(),
                    ActiveTaskCount = group.Count(task =>
                        task.Status == WorkTaskStatus.Open
                        || task.Status == WorkTaskStatus.InProgress
                        || task.Status == WorkTaskStatus.Waiting),
                    CompletedTaskCount = group.Count(task =>
                        task.Status == WorkTaskStatus.Resolved
                        || task.Status == WorkTaskStatus.Closed),
                })
                .ToDictionaryAsync(item => item.MembershipId, cancellationToken);

            var response = members.Select(item =>
            {
                assigneeStats.TryGetValue(item.Id, out var stats);
                var total = stats?.TotalAssignedTaskCount ?? 0;
                var completed = stats?.CompletedTaskCount ?? 0;
                decimal? completionRate = total > 0
                    ? Math.Round((decimal)completed / total * 100m, 1)
                    : null;

                return new TenantMemberResponse(
                    item.Id,
                    item.UserId,
                    item.DisplayName,
                    item.Email,
                    item.Role.ToString(),
                    item.Status.ToString(),
                    item.CreatedAtUtc,
                    null,
                    item.Role is MembershipRole.Owner or MembershipRole.Admin,
                    item.Role is MembershipRole.Owner or MembershipRole.Admin ? null : item.WorkspaceAccessCount,
                    stats?.ActiveTaskCount ?? 0,
                    completed,
                    total,
                    completionRate);
            }).ToList();

            await session.CommitAsync(cancellationToken);
            return Results.Ok(response);
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

    private static async Task<IResult> ReplaceWorkspaceAccessAsync(
        Guid tenantId,
        Guid membershipId,
        ReplaceWorkspaceAccessRequest request,
        ICurrentUser currentUser,
        ITenantRlsSessionFactory sessions,
        WorkspaceAuthorizationService authorization,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        if (request.Grants is null || request.Grants.Count == 0)
        {
            return Results.BadRequest(new { error = "invalid_workspace_access_grants" });
        }

        var parsedGrants = new List<(Guid WorkspaceId, WorkspaceAccessLevel? AccessLevel)>(request.Grants.Count);
        foreach (var grant in request.Grants)
        {
            if (grant.WorkspaceId == Guid.Empty)
            {
                return Results.BadRequest(new { error = "invalid_workspace_access_grants" });
            }

            if (grant.AccessLevel is null)
            {
                parsedGrants.Add((grant.WorkspaceId, null));
                continue;
            }

            if (!Enum.TryParse<WorkspaceAccessLevel>(grant.AccessLevel, true, out var accessLevel))
            {
                return Results.BadRequest(new { error = "invalid_workspace_access_level" });
            }

            parsedGrants.Add((grant.WorkspaceId, accessLevel));
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

            var workspaceIds = parsedGrants.Select(grant => grant.WorkspaceId).Distinct().ToList();
            var existingWorkspaceCount = await session.DbContext.Workspaces
                .AsNoTracking()
                .CountAsync(workspace => workspaceIds.Contains(workspace.Id), cancellationToken);
            if (existingWorkspaceCount != workspaceIds.Count)
            {
                return Results.NotFound(new { error = "workspace_not_found" });
            }

            var existingAccess = await session.DbContext.WorkspaceAccess
                .Where(item => item.MembershipId == membershipId && workspaceIds.Contains(item.WorkspaceId))
                .ToDictionaryAsync(item => item.WorkspaceId, cancellationToken);

            var now = DateTimeOffset.UtcNow;
            foreach (var (workspaceId, accessLevel) in parsedGrants)
            {
                if (accessLevel is null)
                {
                    if (existingAccess.Remove(workspaceId, out var removed))
                    {
                        session.DbContext.WorkspaceAccess.Remove(removed);
                    }

                    continue;
                }

                if (existingAccess.TryGetValue(workspaceId, out var existing))
                {
                    existing.AccessLevel = accessLevel.Value;
                    existing.UpdatedAtUtc = now;
                    continue;
                }

                var created = new WorkspaceAccess
                {
                    Id = Guid.NewGuid(),
                    TenantId = session.TenantId,
                    MembershipId = membershipId,
                    WorkspaceId = workspaceId,
                    AccessLevel = accessLevel.Value,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                };
                session.DbContext.WorkspaceAccess.Add(created);
                existingAccess[workspaceId] = created;
            }

            await session.DbContext.SaveChangesAsync(cancellationToken);

            var response = existingAccess.Values
                .OrderBy(item => item.WorkspaceId)
                .Select(item => new WorkspaceAccessResponse(
                    item.MembershipId,
                    item.WorkspaceId,
                    item.AccessLevel.ToString()))
                .ToList();

            await session.CommitAsync(cancellationToken);
            return Results.Ok(response);
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

    private static async Task<IResult> ListWorkspaceMemberAccessAsync(
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
            if (!authorization.CanManageAccess(session.HasImplicitFullResourceAccess))
            {
                return Results.Json(
                    new { error = "workspace_access_manage_forbidden" },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var workspaceExists = await session.DbContext.Workspaces
                .AsNoTracking()
                .AnyAsync(workspace => workspace.Id == workspaceId, cancellationToken);
            if (!workspaceExists)
            {
                return Results.NotFound(new { error = "workspace_not_found" });
            }

            var explicitAccess = await session.DbContext.WorkspaceAccess
                .AsNoTracking()
                .Where(item => item.WorkspaceId == workspaceId)
                .ToDictionaryAsync(item => item.MembershipId, item => item.AccessLevel, cancellationToken);

            var members = await (
                from membership in session.DbContext.Memberships.AsNoTracking()
                join user in session.DbContext.Users.AsNoTracking()
                    on membership.UserId equals user.Id
                where membership.TenantId == session.TenantId
                orderby user.DisplayName, user.Email
                select new
                {
                    membership.Id,
                    membership.UserId,
                    user.DisplayName,
                    user.Email,
                    membership.Role,
                    membership.Status,
                })
                .ToListAsync(cancellationToken);

            var response = members
                .Select(item =>
                {
                    var hasImplicit = item.Role is MembershipRole.Owner or MembershipRole.Admin
                        && item.Status == MembershipStatus.Active;
                    string effectiveAccess;
                    if (hasImplicit)
                    {
                        effectiveAccess = "Full";
                    }
                    else if (item.Status != MembershipStatus.Active)
                    {
                        effectiveAccess = "None";
                    }
                    else if (explicitAccess.TryGetValue(item.Id, out var level))
                    {
                        effectiveAccess = level.ToString();
                    }
                    else
                    {
                        effectiveAccess = "None";
                    }

                    return new WorkspaceMemberAccessResponse(
                        item.Id,
                        item.UserId,
                        item.DisplayName,
                        item.Email,
                        item.Role.ToString(),
                        item.Status.ToString(),
                        hasImplicit,
                        effectiveAccess);
                })
                .ToList();

            await session.CommitAsync(cancellationToken);
            return Results.Ok(response);
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

    private static async Task<IResult> GrantWorkspaceMemberAccessAsync(
        Guid tenantId,
        Guid workspaceId,
        GrantWorkspaceMemberAccessRequest request,
        ICurrentUser currentUser,
        ITenantRlsSessionFactory sessions,
        WorkspaceAuthorizationService authorization,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        if (request.MembershipIds is null || request.MembershipIds.Count == 0)
        {
            return Results.BadRequest(new { error = "invalid_workspace_access_grants" });
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

            var workspaceExists = await session.DbContext.Workspaces
                .AsNoTracking()
                .AnyAsync(workspace => workspace.Id == workspaceId, cancellationToken);
            if (!workspaceExists)
            {
                return Results.NotFound(new { error = "workspace_not_found" });
            }

            var membershipIds = request.MembershipIds.Distinct().ToList();
            var memberships = await session.DbContext.Memberships
                .AsNoTracking()
                .Where(membership => membershipIds.Contains(membership.Id))
                .ToListAsync(cancellationToken);
            if (memberships.Count != membershipIds.Count)
            {
                return Results.NotFound(new { error = "membership_not_found" });
            }

            foreach (var membership in memberships)
            {
                if (membership.Role != MembershipRole.Member || membership.Status != MembershipStatus.Active)
                {
                    return Results.BadRequest(new { error = "workspace_access_requires_active_member" });
                }
            }

            var existingAccess = await session.DbContext.WorkspaceAccess
                .Where(item => item.WorkspaceId == workspaceId && membershipIds.Contains(item.MembershipId))
                .ToDictionaryAsync(item => item.MembershipId, cancellationToken);

            var now = DateTimeOffset.UtcNow;
            foreach (var membershipId in membershipIds)
            {
                if (existingAccess.TryGetValue(membershipId, out var existing))
                {
                    existing.AccessLevel = accessLevel;
                    existing.UpdatedAtUtc = now;
                    continue;
                }

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

            await session.DbContext.SaveChangesAsync(cancellationToken);
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
        catch (DbUpdateException)
        {
            return Results.BadRequest(new { error = "invalid_workspace_access_relationship" });
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

    private static string? NormalizeDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        return description.Trim();
    }

    private static DateTimeOffset ResolveUpdatedAtUtc(Workspace workspace)
        => workspace.UpdatedAtUtc ?? workspace.CreatedAtUtc;

    private static WorkspaceResponse MapWorkspace(
        Workspace workspace,
        string accessLevel,
        bool canManage) =>
        new(
            workspace.Id,
            workspace.TenantId,
            workspace.Name,
            workspace.Description,
            workspace.StartDate,
            workspace.CreatedAtUtc,
            ResolveUpdatedAtUtc(workspace),
            accessLevel,
            canManage);

    private static string? NormalizeProjectDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        return description.Trim();
    }

    private static async Task<(Dictionary<Guid, int> OpenCounts, Dictionary<Guid, int> TaskCounts)> LoadProjectTaskCountsAsync(
        TenantRlsSession session,
        IReadOnlyList<Guid> projectIds,
        CancellationToken cancellationToken)
    {
        if (projectIds.Count == 0)
        {
            return ([], []);
        }

        var taskCounts = await session.DbContext.WorkTasks
            .AsNoTracking()
            .Where(task => task.TenantId == session.TenantId && projectIds.Contains(task.ProjectId))
            .GroupBy(task => task.ProjectId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Key, item => item.Count, cancellationToken);

        var openCounts = await session.DbContext.WorkTasks
            .AsNoTracking()
            .Where(task =>
                task.TenantId == session.TenantId &&
                projectIds.Contains(task.ProjectId) &&
                (task.Status == WorkTaskStatus.Open
                 || task.Status == WorkTaskStatus.InProgress
                 || task.Status == WorkTaskStatus.Waiting))
            .GroupBy(task => task.ProjectId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Key, item => item.Count, cancellationToken);

        return (openCounts, taskCounts);
    }

    private static ProjectResponse MapProject(Project project, int openTaskCount, int taskCount = 0) =>
        new(
            project.Id,
            project.TenantId,
            project.WorkspaceId,
            project.Name,
            project.Description,
            project.AccentToken,
            openTaskCount,
            taskCount,
            project.CreatedAtUtc);
}

public sealed record CreateWorkspaceRequest(
    string Name,
    string? Description = null,
    DateOnly? StartDate = null,
    Guid? TenantId = null);

public sealed record UpdateWorkspaceRequest(
    string Name,
    string? Description = null,
    DateOnly? StartDate = null);

public sealed record CreateProjectRequest(
    string Name,
    string? Description = null,
    string? AccentToken = null,
    Guid? TenantId = null);

public sealed record UpdateProjectRequest(
    string Name,
    string? Description = null,
    string? AccentToken = null);

public sealed record SetWorkspaceAccessRequest(string AccessLevel);

public sealed record ReplaceWorkspaceAccessRequest(IReadOnlyList<WorkspaceAccessGrantRequest> Grants);

public sealed record WorkspaceAccessGrantRequest(Guid WorkspaceId, string? AccessLevel);

public sealed record GrantWorkspaceMemberAccessRequest(
    IReadOnlyList<Guid> MembershipIds,
    string AccessLevel);

public sealed record WorkspaceResponse(
    Guid WorkspaceId,
    Guid TenantId,
    string Name,
    string? Description,
    DateOnly? StartDate,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string AccessLevel,
    bool CanManage);

public sealed record ProjectResponse(
    Guid ProjectId,
    Guid TenantId,
    Guid WorkspaceId,
    string Name,
    string? Description,
    string? AccentToken,
    int OpenTaskCount,
    int TaskCount,
    DateTimeOffset CreatedAtUtc);

public sealed record TenantMemberResponse(
    Guid MembershipId,
    Guid UserId,
    string DisplayName,
    string Email,
    string Role,
    string Status,
    DateTimeOffset JoinedAtUtc,
    string? AvatarUrl,
    bool HasImplicitWorkspaceAccess,
    int? WorkspaceAccessCount,
    int ActiveTaskCount,
    int CompletedTaskCount,
    int TotalAssignedTaskCount,
    decimal? CompletionRate);

public sealed record WorkspaceAccessResponse(Guid MembershipId, Guid WorkspaceId, string AccessLevel);

public sealed record WorkspaceMemberAccessResponse(
    Guid MembershipId,
    Guid UserId,
    string DisplayName,
    string Email,
    string Role,
    string Status,
    bool HasImplicitWorkspaceAccess,
    string EffectiveAccess);
