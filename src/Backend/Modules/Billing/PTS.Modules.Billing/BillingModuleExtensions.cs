using Microsoft.Extensions.DependencyInjection;

namespace PTS.Modules.Billing;

/// <summary>
/// Composition entry point for the Billing module (subscriptions, payment
/// provider integration, invoices).
/// </summary>
public static class BillingModuleExtensions
{
    /// <summary>
    /// Registers Billing module services with the DI container.
    /// Phase 1: architectural placeholder only — no subscriptions, payment
    /// provider integration (e.g. Stripe), or invoicing is implemented yet.
    /// See README.md.
    /// </summary>
    public static IServiceCollection AddBillingModule(this IServiceCollection services)
    {
        return services;
    }
}
