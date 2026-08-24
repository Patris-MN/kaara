using Microsoft.EntityFrameworkCore;
using PTS.Host.TenantAccess;
using PTS.Modules.Identity;
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
        tenant.MapPost("/workspaces/{workspaceId:guid}/projects", CreateProjectAsync);
        tenant.MapGet("/workspaces/{workspaceId:guid}/projects", ListProjectsAsync);

        return endpoints;
    }

    private static async Task<IResult> CreateWorkspaceAsync(
        Guid tenantId,
        CreateWorkspaceRequest request,
        ICurrentUser currentUser,
        ITenantRlsSessionFactory sessions,
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
                new WorkspaceResponse(workspace.Id, workspace.TenantId, workspace.Name));
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
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        try
        {
            await using var session = await sessions.OpenAsync(tenantId, cancellationToken);
            var workspaces = await session.DbContext.Workspaces.AsNoTracking().ToListAsync(cancellationToken);
            await session.CommitAsync(cancellationToken);
            return Results.Ok(workspaces.Select(w => new WorkspaceResponse(w.Id, w.TenantId, w.Name)));
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
}

public sealed record CreateWorkspaceRequest(string Name, Guid? TenantId = null);

public sealed record CreateProjectRequest(string Name, Guid? TenantId = null);

public sealed record WorkspaceResponse(Guid WorkspaceId, Guid TenantId, string Name);

public sealed record ProjectResponse(Guid ProjectId, Guid TenantId, Guid WorkspaceId, string Name);
