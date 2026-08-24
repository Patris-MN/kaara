# Tenancy & Membership Module

## Bounded Context Responsibility

Owns **tenants (organizations)** and **Membership** — the link between a global
`User` (Identity module) and a tenant, plus the tenant-level roles/permissions that
apply only within that tenant.

In scope (future phases):

- `Tenant` record (organization) and its lifecycle (created, suspended, etc.).
- `Membership` record: `UserId` + `TenantId` + tenant-level role(s).
- Resolving the **authenticated tenant context** for a request: given the
  authenticated user, determine which tenant they are acting in and verify an
  active `Membership` exists, server-side. This is the *only* legitimate source of
  `TenantId` for the rest of the request pipeline — never the client.
- Enterprise isolation strategy selection per tenant (shared schema with RLS today;
  dedicated schema/database is a future option this module must be able to express
  without changing consuming modules — see architecture charter).

Explicitly **out of scope**:

- Authentication/credentials (Identity module).
- Platform-operator permissions (PlatformAdministration module) — a platform admin
  is not a tenant Membership.

## Allowed dependencies

- `PTS.SharedKernel` only.
- Must **not** reference `PTS.Modules.Identity`, `PTS.Modules.WorkManagement`, or any
  other module directly.

## Security note

Every other module that owns tenant-scoped data depends on this module having
established tenant context correctly. Rows are still protected independently by
PostgreSQL Row-Level Security — this module setting tenant context is defense in
depth, not the only line of defense.

## Phase 1 status

Architectural placeholder only. No tenant/membership persistence or resolution logic
is implemented in this phase.
