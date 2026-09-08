namespace PTS.Modules.Tenancy;

/// <summary>
/// The caller has no Active Owner/Admin membership for the tenant they
/// attempted to update. Mapped to HTTP 403 — do not leak tenant existence.
/// </summary>
public sealed class TenantUpdateForbiddenException : Exception
{
    public TenantUpdateForbiddenException()
        : base("Active Owner or Admin membership is required to update this organization.")
    {
    }
}
