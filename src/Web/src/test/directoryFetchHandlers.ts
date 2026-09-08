import type { AccountCapabilities, TenantMembership } from "../api/types";

export function deriveAccountCapabilities(
  tenants: Pick<TenantMembership, "role">[],
  invitations: unknown[] = [],
): AccountCapabilities {
  const ownerCount = tenants.filter((tenant) => tenant.role === "Owner").length;
  const activeCount = tenants.length;
  const pendingCount = invitations.length;
  const canCreateOrganization = ownerCount > 0 || (activeCount === 0 && pendingCount === 0);

  return {
    canCreateOrganization,
    organizationLimit: ownerCount > 0 ? -1 : 1,
    currentOrganizationCount: ownerCount,
    activeMembershipCount: activeCount,
    pendingInvitationCount: pendingCount,
  };
}

export function handleDirectoryFetch(
  pathName: string,
  method: string,
  tenants: TenantMembership[],
  invitations: TenantMembership[] = [],
  authUser: unknown,
): Response | null {
  const json = (body: unknown, status = 200) =>
    new Response(JSON.stringify(body), {
      status,
      headers: { "Content-Type": "application/json" },
    });

  if (pathName === "/auth/me") {
    return json(authUser);
  }
  if (pathName === "/tenants" && method === "GET") {
    return json(tenants);
  }
  if (pathName === "/invitations" && method === "GET") {
    return json(invitations);
  }
  if (pathName === "/account/capabilities") {
    return json(deriveAccountCapabilities(tenants, invitations));
  }
  if (pathName === "/notifications") {
    return json({ items: [], unreadCount: 0 });
  }
  return null;
}
