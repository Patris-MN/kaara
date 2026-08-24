using Microsoft.Extensions.DependencyInjection;

namespace PTS.Modules.Audit;

/// <summary>
/// Composition entry point for the Audit &amp; Logging module.
/// </summary>
public static class AuditModuleExtensions
{
    /// <summary>
    /// Registers Audit module services with the DI container.
    /// Phase 1: architectural placeholder only — no audit event schema, storage,
    /// or capture pipeline is implemented yet. See README.md.
    /// </summary>
    public static IServiceCollection AddAuditModule(this IServiceCollection services)
    {
        return services;
    }
}
