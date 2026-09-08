# ADR 0012: Global Account Authorization vs Tenant Role vs Resource Access

## Status

Accepted — Phase 8.1.3

## Context

PTS authorization spans three scopes that must not be conflated:

1. **Global / account scope** — capabilities outside any Organization (for example `CanCreateOrganization`).
2. **Tenant / organization role** — Owner, Admin, Member authority inside a Membership.
3. **Resource access** — Workspace-level No access / View / Edit for ordinary Members.

Before Phase 8.1.3, any authenticated user could create Organizations via `POST /tenants`, and the frontend always showed **+ New organization**. Tenant role was incorrectly treated as a proxy for global account entitlement.

Organization creation happens **outside** the current tenant context. Membership role must never imply global capabilities.

## Decision

### Separate evaluation chains

**Global account**

```
User → Account entitlement / policy → CanCreateOrganization
```

**Inside an Organization**

```
User → Active Membership → Tenant Role → WorkspaceAccess → Project inheritance → Task authorization → PostgreSQL RLS
```

Do not weaken the tenant chain. Do not infer global capabilities from `Membership.Role`.

### Centralized organization creation seam

- Contract: `IOrganizationCreationEntitlementProvider` in `PTS.SharedKernel` returning `OrganizationCreationEntitlement` (`CanCreateOrganization`, `OrganizationLimit`, `CurrentOrganizationCount`, counters for active memberships and pending invitations).
- HTTP: `GET /account/capabilities` exposes the effective global account capability snapshot to the frontend.
- Enforcement: `POST /tenants` consults the same provider and returns **403** `organization_create_forbidden` when disallowed.

Future Billing/Entitlements will replace the Host development provider without scattering checks across controllers or UI.

### Pre-Billing development policy (temporary)

Implemented in `DevelopmentOrganizationCreationEntitlementProvider` (`PTS.Host`):

| Condition | `CanCreateOrganization` |
|-----------|-------------------------|
| Active Owner membership count ≥ 1 | **true** (unlimited additional orgs for multi-org testing; `OrganizationLimit = -1`) |
| Active memberships = 0 **and** pending invitations = 0 | **true** (fresh independent signup — first org) |
| Active memberships = 0 **and** pending invitations > 0 | **false** (interrupted invite onboarding — recover invitation first) |
| Active membership exists but user is never Owner | **false** (Admin/Member only) |

**Who receives the temporary capability today**

- Fresh users who register directly with zero memberships and no pending invitations.
- Users who already own at least one Organization (development multi-org testing).

**Who does not**

- Invited Members/Admins without an Owner membership elsewhere.
- Users with pending invitations but no active memberships (until they accept or invitations expire).

This policy is **not** the commercial entitlement model. It must be replaced by plan-derived entitlements without changing call sites.

### No global “invited user” account type

There is no `User.IsInvitedUser` or permanent account classification. One global User may simultaneously be Owner in Org A, Member in Org B, and have a pending invitation to Org D. Memberships and invitations express relationships.

### Resource creation baseline (system roles)

| Action | Owner | Admin | Member |
|--------|-------|-------|--------|
| Create Organization | Global entitlement | Global entitlement | Global entitlement |
| Create Workspace | Yes | Yes | No |
| Create Project | Yes | Yes | Only with Workspace **Edit** |
| Manage workspace access / invite members | Per existing capabilities | Per existing capabilities | No (default) |

`WorkspaceAccess.Edit` does **not** grant create-new-Workspace. It applies to an existing Workspace.

### Zero-membership authenticated state

`Active Memberships = 0` is valid. The app shows intentional onboarding:

- **State A:** independent signup + creation capability → welcome + create first organization.
- **State B:** pending invitations → list invitations + secure continue (accept via server; no raw token exposure).
- **State C:** no capability and no invitations → neutral explanation.

### Interrupted invitation recovery

When a user registers through `/invite/{token}` but accepts later, login surfaces pending invitations from `GET /invitations`. Recovery uses **Continue invitation** → `POST /tenants/{tenantId}/invitations/accept` (existing secure flow). Raw tokens are never reconstructed from hashes.

## Consequences

- Frontend gates **+ New organization** from `/account/capabilities`; unknown/missing capability fails closed.
- Backend remains authoritative for all create endpoints.
- Future work: Billing-derived `OrganizationLimit`, feature entitlements, and role capability catalog plug into SharedKernel contracts and Host composition — not ad hoc controller checks.

## Related

- ADR 0011 — Secure invitation link onboarding
- Rule `20-identity-membership-platform-admin.mdc`
- Rule `40-entitlements-and-billing.mdc`
