using Microsoft.EntityFrameworkCore;
using Npgsql;
using PTS.Host.Persistence;
using PTS.Modules.WorkManagement;
using PTS.SharedKernel.Text;

namespace PTS.Host.Http;

internal static class ResourceNameConflicts
{
    internal static (string DisplayName, string NormalizedName) ParseName(string raw)
    {
        var displayName = raw.Trim();
        return (displayName, ResourceNameNormalizer.Normalize(displayName));
    }

    internal static async Task<string?> FindWorkspaceDisplayNameAsync(
        AppDbContext db,
        Guid tenantId,
        string normalizedName,
        Guid? excludeWorkspaceId,
        CancellationToken cancellationToken)
    {
        var query = db.Workspaces.AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.NameNormalized == normalizedName);
        if (excludeWorkspaceId is Guid excluded)
        {
            query = query.Where(item => item.Id != excluded);
        }

        return await query.Select(item => item.Name).FirstOrDefaultAsync(cancellationToken);
    }

    internal static async Task<string?> FindProjectDisplayNameAsync(
        AppDbContext db,
        Guid tenantId,
        Guid workspaceId,
        string normalizedName,
        Guid? excludeProjectId,
        CancellationToken cancellationToken)
    {
        var query = db.Projects.AsNoTracking()
            .Where(item =>
                item.TenantId == tenantId &&
                item.WorkspaceId == workspaceId &&
                item.NameNormalized == normalizedName);
        if (excludeProjectId is Guid excluded)
        {
            query = query.Where(item => item.Id != excluded);
        }

        return await query.Select(item => item.Name).FirstOrDefaultAsync(cancellationToken);
    }

    internal static bool IsUniqueViolation(DbUpdateException exception)
        => exception.InnerException is PostgresException pg
           && pg.SqlState == PostgresErrorCodes.UniqueViolation;

    internal static void ApplyWorkspaceName(Workspace workspace, string displayName)
    {
        workspace.Name = displayName;
        workspace.NameNormalized = ResourceNameNormalizer.Normalize(displayName);
    }

    internal static void ApplyProjectName(Project project, string displayName)
    {
        project.Name = displayName;
        project.NameNormalized = ResourceNameNormalizer.Normalize(displayName);
    }
}
