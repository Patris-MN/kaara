using PTS.Host.Http;
using PTS.Modules.WorkManagement;

namespace PTS.IntegrationTests;

internal static class TestProjectFactory
{
    internal static CreateProjectRequest CreateRequest(
        string name,
        string? description = null,
        string? accentToken = null,
        Guid? tenantId = null)
        => new(name, description, accentToken, tenantId);

    internal static Project SeedProject(
        Guid tenantId,
        Guid workspaceId,
        string name,
        string? accentToken = null)
        => new Project
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            WorkspaceId = workspaceId,
            Name = name,
            NameNormalized = name.Trim().ToLowerInvariant(),
            AccentToken = accentToken ?? ProjectAccentRules.Default,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

    internal static WorkTask SeedWorkTask(
        Guid tenantId,
        Guid workspaceId,
        Guid projectId,
        Guid createdByMembershipId,
        string title,
        WorkTaskStatus status = WorkTaskStatus.Open,
        WorkTaskPriority priority = WorkTaskPriority.Normal,
        Guid? assignedMembershipId = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new WorkTask
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            WorkspaceId = workspaceId,
            ProjectId = projectId,
            Title = title,
            Status = status,
            Priority = priority,
            CreatedByMembershipId = createdByMembershipId,
            AssignedMembershipId = assignedMembershipId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
    }
}
