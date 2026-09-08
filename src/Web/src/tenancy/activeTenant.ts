export function resolveActiveTenantId(
  tenants: ReadonlyArray<{ tenantId: string }>,
  routeTenantId: string | undefined,
  storedTenantId: string | null,
): string | undefined {
  if (routeTenantId && tenants.some((tenant) => tenant.tenantId === routeTenantId)) {
    return routeTenantId;
  }
  if (storedTenantId && tenants.some((tenant) => tenant.tenantId === storedTenantId)) {
    return storedTenantId;
  }
  return undefined;
}
