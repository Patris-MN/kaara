using Microsoft.Extensions.DependencyInjection;

namespace PTS.Modules.WorkManagement;

/// <summary>
/// Composition entry point for the Work Management module (workspaces, projects,
/// tasks, comments — the product's core business domain).
/// </summary>
public static class WorkManagementModuleExtensions
{
    /// <summary>
    /// Registers Work Management module services with the DI container.
    /// Phase 4: registers nothing beyond the module marker; Workspace/Project
    /// persistence is composed in the Host. See README.md.
    /// </summary>
    public static IServiceCollection AddWorkManagementModule(this IServiceCollection services)
    {
        services.AddScoped<WorkspaceAuthorizationService>();
        services.AddScoped<TaskStatusWorkflow>();
        services.AddScoped<TaskAuthorizationService>();
        return services;
    }
}
