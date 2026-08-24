using Microsoft.Extensions.DependencyInjection;

namespace PTS.Modules.PlatformAdministration;

/// <summary>
/// Composition entry point for the Platform Administration module (internal
/// operator/staff capabilities, distinct from any tenant-level role).
/// </summary>
public static class PlatformAdministrationModuleExtensions
{
    /// <summary>
    /// Registers Platform Administration module services with the DI container.
    /// Phase 1: architectural placeholder only — no platform-admin roles or
    /// operator tooling is implemented yet. See README.md.
    /// </summary>
    public static IServiceCollection AddPlatformAdministrationModule(this IServiceCollection services)
    {
        return services;
    }
}
