using Microsoft.Extensions.DependencyInjection;

namespace PTS.Modules.Storage;

/// <summary>
/// Composition entry point for the Storage module (file/blob storage abstraction).
/// </summary>
public static class StorageModuleExtensions
{
    /// <summary>
    /// Registers Storage module services with the DI container.
    /// Phase 1: architectural placeholder only — no storage provider, upload
    /// endpoints, or key-naming implementation exists yet. See README.md.
    /// </summary>
    public static IServiceCollection AddStorageModule(this IServiceCollection services)
    {
        return services;
    }
}
